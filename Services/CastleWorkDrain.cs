using ProjectM.CastleBuilding;
using System;
using System.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal static class CastleWorkDrain
    {
        static bool ParkIfRebuilding(WorkQueueService q, int plot, Entity heart)
        {
            if (!Core.TerritoryService.IsTerritoryRebuilding(plot)
                && heart.Read<CastleRebuildPhaseState>().State == PhaseState.None)
                return false;
            q.rebuildDeferred.Add(plot);
            return true;
        }

        public static IEnumerator Loop(WorkQueueService q)
        {
            yield return null;
            Core.TerritoryService.StartTimer();
            for (;;)
            {
                if (q.pending.Count == 0)
                {
                    q.DrainGeneration++;
                    yield return null;
                    Core.TerritoryService.StartTimer();
                    continue;
                }

                if (Core.TerritoryService.ShouldUpdateYield())
                {
                    yield return null;
                    Core.TerritoryService.StartTimer();
                }

                var plot = q.pending.Dequeue();
                q.queued.Remove(plot);

                var heart = Core.TerritoryService.GetCastleHeart(plot);
                if (heart == Entity.Null)
                    continue;
                if (ParkIfRebuilding(q, plot, heart))
                    continue;

                q.processing = plot;
                q.reprocessCurrent = false;

                var callbacks = Core.TerritoryService.UpdateCallbacks;
                for (var c = 0; c < callbacks.Count; c++)
                {
                    IEnumerator step = null;
                    var running = false;
                    try
                    {
                        step = callbacks[c](plot, heart);
                        running = step.MoveNext();
                    }
                    catch (Exception e)
                    {
                        Core.LogException(e);
                    }

                    while (running)
                    {
                        yield return null;
                        Core.TerritoryService.StartTimer();
                        try
                        {
                            running = step.MoveNext();
                        }
                        catch (Exception e)
                        {
                            Core.LogException(e);
                            running = false;
                        }
                    }

                    if (Core.TerritoryService.ShouldUpdateYield())
                    {
                        yield return null;
                        Core.TerritoryService.StartTimer();
                    }
                }

                q.processing = -1;
                if (q.reprocessCurrent)
                    q.Enqueue(plot);
            }
        }
    }
}
