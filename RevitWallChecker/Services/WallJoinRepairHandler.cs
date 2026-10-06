using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Text;

namespace RevitWallChecker.Services
{
    public class WallJoinRepairRequest
    {
        public int WallId { get; set; }

        // 0 = start point, 1 = end point
        public int WallEnd { get; set; }

        public int NearbyWallId { get; set; }
    }

    public class WallJoinRepairHandler : IExternalEventHandler
    {
        public List<WallJoinRepairRequest> Requests { get; set; }
            = new List<WallJoinRepairRequest>();

        public Action<string> OnCompleted { get; set; }

        // Must be consistent with the tolerance used by
        // CheckUnconnectedWalls(). This is in Revit internal feet.
        // 0.1 feet is approximately 30.48 mm.
        private const double RepairToleranceFeet = 0.1;

        public void Execute(UIApplication app)
        {
            int changedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            StringBuilder details = new StringBuilder();

            UIDocument uiDoc = app.ActiveUIDocument;

            if (uiDoc == null)
            {
                OnCompleted?.Invoke(
                    "Wall join repair cancelled: no active Revit document.");
                return;
            }

            Document doc = uiDoc.Document;

            if (doc.IsReadOnly)
            {
                OnCompleted?.Invoke(
                    "Wall join repair cancelled: the document is read-only.");
                return;
            }

            List<WallJoinRepairRequest> requests =
                Requests ?? new List<WallJoinRepairRequest>();

            if (requests.Count == 0)
            {
                OnCompleted?.Invoke("No wall-join repair requests were supplied.");
                return;
            }

            using (Transaction tx =
                   new Transaction(doc, "Allow Wall End Joins"))
            {
                try
                {
                    tx.Start();

                    foreach (WallJoinRepairRequest request in requests)
                    {
                        if (request == null)
                        {
                            skippedCount++;
                            details.AppendLine(
                                "Skipped a null repair request.");
                            continue;
                        }

                        try
                        {
                            if (request.WallEnd != 0 &&
                                request.WallEnd != 1)
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}: invalid wall end " +
                                    $"{request.WallEnd}. Expected 0 or 1.");
                                continue;
                            }

                            Wall wall = doc.GetElement(
                                new ElementId(request.WallId)) as Wall;

                            Wall nearbyWall = doc.GetElement(
                                new ElementId(request.NearbyWallId)) as Wall;

                            if (wall == null || nearbyWall == null)
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}, nearby wall " +
                                    $"{request.NearbyWallId}: one or both walls " +
                                    "could not be found.");
                                continue;
                            }

                            if (wall.Id == nearbyWall.Id)
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}: the nearby wall " +
                                    "ID refers to the same wall.");
                                continue;
                            }

                            LocationCurve location =
                                wall.Location as LocationCurve;

                            LocationCurve nearbyLocation =
                                nearbyWall.Location as LocationCurve;

                            Curve wallCurve = location?.Curve;
                            Curve nearbyCurve = nearbyLocation?.Curve;

                            if (wallCurve == null || nearbyCurve == null)
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}: a wall location " +
                                    "curve could not be obtained.");
                                continue;
                            }

                            XYZ endpoint = wallCurve.GetEndPoint(
                                request.WallEnd);

                            IntersectionResult projection =
                                nearbyCurve.Project(endpoint);

                            if (projection == null)
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}, end " +
                                    $"{request.WallEnd}: projection onto nearby " +
                                    $"wall {request.NearbyWallId} failed.");
                                continue;
                            }

                            double distance = projection.Distance;

                            if (distance > RepairToleranceFeet)
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}, end " +
                                    $"{request.WallEnd}: endpoint is {distance:F4} " +
                                    "feet from nearby wall " +
                                    $"{request.NearbyWallId}, exceeding the " +
                                    $"{RepairToleranceFeet:F3}-foot repair tolerance.");
                                continue;
                            }

                            // Do not count an already-allowed join as a change.
                            if (WallUtils.IsWallJoinAllowedAtEnd(
                                wall, request.WallEnd))
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}, end " +
                                    $"{request.WallEnd}: joining is already allowed.");
                                continue;
                            }

                            WallUtils.AllowWallJoinAtEnd(
                                wall, request.WallEnd);

                            // Verify that the permission was changed.
                            if (WallUtils.IsWallJoinAllowedAtEnd(
                                wall, request.WallEnd))
                            {
                                changedCount++;
                            }
                            else
                            {
                                skippedCount++;
                                details.AppendLine(
                                    $"Wall {request.WallId}, end " +
                                    $"{request.WallEnd}: Revit did not enable " +
                                    "the join permission.");
                            }
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            details.AppendLine(
                                $"Wall {request.WallId}, end " +
                                $"{request.WallEnd}: {ex.Message}");
                        }
                    }

                    if (changedCount > 0)
                    {
                        TransactionStatus status = tx.Commit();

                        if (status != TransactionStatus.Committed)
                        {
                            failedCount += changedCount;
                            changedCount = 0;
                            details.AppendLine(
                                "Revit did not commit the transaction. " +
                                "The requested changes were not confirmed.");
                        }
                    }
                    else
                    {
                        tx.RollBack();
                    }
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();

                    failedCount++;
                    details.AppendLine(
                        "Transaction error: " + ex.Message);
                }
            }

            StringBuilder summary = new StringBuilder();

            summary.AppendLine("Wall join repair completed.");
            summary.AppendLine(
                $"Join permissions changed: {changedCount}");
            summary.AppendLine($"Skipped: {skippedCount}");
            summary.AppendLine($"Failed: {failedCount}");

            if (details.Length > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Details:");
                summary.Append(details);
            }

            OnCompleted?.Invoke(summary.ToString());
        }

        public string GetName()
        {
            return "Wall Join Permission Repair";
        }
    }
}
