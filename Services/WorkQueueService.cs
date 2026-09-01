
namespace Satisvampory.Services;

internal class WorkQueueService
{
    internal readonly HashSet<int> queued = [];
    internal readonly Queue<int> pending = new();
    internal readonly HashSet<int> rebuildDeferred = [];
    internal int processing = -1;
    internal bool reprocessCurrent;
    int transferSuppress;
    internal int DrainGeneration { get; set; }

    public WorkQueueService() => Core.StartCoroutine(CastleWorkDrain.Loop(this));

    public int QueueDepth => pending.Count;
    public bool IsSelfTransferring => transferSuppress > 0;
    public void BeginSelfTransfer() => transferSuppress++;
    public void EndSelfTransfer() => transferSuppress = transferSuppress > 0 ? transferSuppress - 1 : 0;
    public bool IsQueued(int territoryId) => territoryId == processing || queued.Contains(territoryId);

    public void Enqueue(int plot)
    {
        if (plot is < TerritoryService.MIN_TERRITORY_ID or > TerritoryService.MAX_TERRITORY_ID)
            return;
        Core.Stash?.InvalidateTerritory(plot);
        ClanTreasuryLend.MarkDirty(plot);
        if (plot == processing) { reprocessCurrent = true; return; }
        if (queued.Add(plot)) pending.Enqueue(plot);
    }

    public void EnqueueOwner(Entity owner)
    {
        if (owner != Entity.Null)
            Enqueue(Core.TerritoryService.GetTerritoryId(owner));
    }

    public void EnqueueAll()
    {
        for (var plot = TerritoryService.MIN_TERRITORY_ID; plot <= TerritoryService.MAX_TERRITORY_ID; plot++)
            if (Core.TerritoryService.GetCastleHeart(plot) != Entity.Null)
                Enqueue(plot);
    }

    internal void FlushRebuildDeferred()
    {
        if (rebuildDeferred.Count == 0) return;
        var parked = new List<int>(rebuildDeferred);
        rebuildDeferred.Clear();
        for (var i = 0; i < parked.Count; i++)
            Enqueue(parked[i]);
    }
}
