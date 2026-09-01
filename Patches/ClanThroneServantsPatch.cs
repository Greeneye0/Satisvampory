namespace Satisvampory.Patches
{
    /// <summary>
    /// ClanShare throne listing is disabled. Harmony postfix on GetResponseEntries
    /// (ref FixedList4096Bytes) and GetAllServants (NativeList.Add) made Burst
    /// SerializeAndSendServerEventsSystem abort the dedicated process on sit.
    /// </summary>
    public static class ClanThroneServantsPatch
    {
    }
}
