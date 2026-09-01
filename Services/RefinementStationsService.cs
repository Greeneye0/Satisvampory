using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services;

internal sealed class RefinementStationsService
{
        readonly HeartBoundIndex index;
        readonly Regex receiveToken;
        readonly Regex sendToken;

        static ComponentType[] QueryTypes =>
        [
            ComponentType.ReadOnly(Il2CppType.Of<Team>()),
            ComponentType.ReadOnly(Il2CppType.Of<CastleHeartConnection>()),
            ComponentType.ReadOnly(Il2CppType.Of<Refinementstation>()),
            ComponentType.ReadOnly(Il2CppType.Of<NameableInteractable>()),
            ComponentType.ReadOnly(Il2CppType.Of<UserOwner>()),
            ComponentType.ReadOnly(Il2CppType.Of<RefinementstationRecipesBuffer>()),
            ComponentType.ReadOnly(Il2CppType.Of<CastleWorkstation>()),
        ];

        public RefinementStationsService() { receiveToken = new Regex(Const.RECEIVER_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase); sendToken = new Regex(Const.SENDER_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase); index = HeartBoundIndex.Scan(includeDisabled: true, QueryTypes); }

        internal void AddRefinementStation(Entity station) => index.Track(station);
        internal void RemoveRefinementStation(Entity station) => index.Untrack(station);

        public IEnumerable<(int group, Entity station)> GetAllReceivingStations(int territoryId) =>
            NamedGroups(receiveToken, territoryId);

        public IEnumerable<(int group, Entity station)> GetAllSendingStations(int territoryId) =>
            NamedGroups(sendToken, territoryId);

        public IEnumerable<Entity> GetAllStationsOnTerritory(int territoryId) => index.OnTerritory(territoryId);

        IEnumerable<(int group, Entity station)> NamedGroups(Regex token, int territoryId)
        {
            foreach (var station in index.OnTerritory(territoryId)) { if (!station.Has<NameableInteractable>()) continue; var plate = station.Read<NameableInteractable>().Name.ToString(); if (string.IsNullOrEmpty(plate)) continue; foreach (Match hit in token.Matches(plate)) if (int.TryParse(hit.Groups[1].Value, out var group)) yield return (group, station); }
        }
}
