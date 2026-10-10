using System;
using System.Collections.Generic;
using ExaltHUD.Models;
using ExaltHUD.Protocol;
using ExaltHUD.Protocol.Packets;

namespace ExaltHUD.Tracking;

// Passive state only. All calls run on the UI thread, together with HUD commands.
internal sealed class TrackingSession : IDisposable
{
    public int ClientId;
    public MapObject Player;
    public MapObject FollowTargetObject;
    public readonly Dictionary<int, MapObject> Entities = new Dictionary<int, MapObject>();
    public readonly Dictionary<int, MapObject> Enemies = new Dictionary<int, MapObject>();
    // The inherited tracker uses "Portals" as its lookup of all visible objects.
    public readonly Dictionary<int, MapObject> Portals = new Dictionary<int, MapObject>();
    public readonly DpsTracker Dps;
    public readonly MoonlightVillageTracker Moonlight;
    private int previousTickId = -1;
    private bool hasMap;
    public event Action<string> Notice;

    public TrackingSession()
    {
        Dps = new DpsTracker(this);
        Moonlight = new MoonlightVillageTracker(this);
    }

    public void BeginConnection()
    {
        ClientId = 0;
        hasMap = false;
        ClearEntities();
    }

    private void ClearEntities()
    {
        Player = null;
        FollowTargetObject = null;
        Entities.Clear();
        Enemies.Clear();
        Portals.Clear();
        previousTickId = -1;
    }

    public void Process(Packet packet)
    {
        if (packet is MapInfoPacket map)
        {
            ClearEntities();
            Dps.OnMapChange(map);
            Moonlight.OnMapInfo(map);
            hasMap = true;
            return;
        }
        if (packet is GotoPacket identity)
        {
            ClientId = identity.ObjectId;
            if (Entities.TryGetValue(ClientId, out var player)) Player = player;
            return;
        }
        if (packet is CrucibleResponsePacket events)
        {
            Dps.OnCrucibleResponse(events);
            return;
        }
        if (!hasMap) return;

        switch (packet)
        {
            case DamageBoostPacket boost: Dps.OnDamageBoost(boost); break;
            case UpdatePacket update:
                foreach (var obj in update.NewObjects)
                {
                    var entity = new MapObject(obj);
                    // Remove an earlier object with a reused ID before classifying it again.
                    Entities.Remove(entity.ObjectId);
                    Enemies.Remove(entity.ObjectId);
                    Portals[entity.ObjectId] = entity;
                    if (entity.IsEnemy) Enemies[entity.ObjectId] = entity;
                    if (entity.IsPlayer) Entities[entity.ObjectId] = entity;
                    if (entity.ObjectId == ClientId) Player = entity;
                    if (entity.ObjectId == ClientId + 1 && entity.ObjectDef?.Pet == true) FollowTargetObject = entity;
                }
                foreach (int id in update.Drops)
                {
                    Entities.Remove(id);
                    Enemies.Remove(id);
                    Portals.Remove(id);
                    if (FollowTargetObject?.ObjectId == id) FollowTargetObject = null;
                }
                Dps.OnUpdate(update);
                Moonlight.OnUpdate(update);
                break;
            case NewTickPacket tick:
                foreach (var stats in tick.Statuses)
                {
                    if (Portals.TryGetValue(stats.ObjectId, out var entity))
                        entity.UpdateFromObjectStatsData(stats, tick.TickTime, tick.TickId, previousTickId, Environment.TickCount);
                }
                previousTickId = tick.TickId;
                Dps.OnNewTick(tick);
                Moonlight.OnNewTick(tick);
                break;
            case MovePacket move:
                if (Player != null && move.Positions.Length > 0)
                    Player.Position = move.Positions[move.Positions.Length - 1].GetWorldPos();
                break;
            case PlayerShootPacket shot: Dps.OnPlayerShoot(shot); break;
            case PlayerShootServerPacket shot: Dps.OnPlayerShootServer(shot); break;
            case ServerPlayerShootPacket shot: Dps.OnServerPlayerShoot(shot); break;
            case EnemyHitPacket hit: Dps.OnEnemyHit(hit); break;
            case EnemyShootPacket damage: Dps.OnEnemyShootDamage(damage); break;
            case UseItemPacket use: Dps.OnUseItem(use); break;
            case StasisPacket stasis: Dps.OnStasis(stasis); break;
            case TextPacket text:
                Dps.OnText(text);
                Moonlight.OnText(text);
                break;
            case NotificationPacket notification:
                Dps.OnNotification(notification);
                break;
            case ShowEffectPacket effect:
                Dps.OnShowEffect(effect);
                break;
            case IncomingPartyMemberInfoPacket party: Dps.OnPartyMemberInfo(party); break;
            case PartyMemberAddedPacket added: Dps.OnPartyMemberAdded(added); break;
            case PartyActionResultPacket result: Dps.OnPartyActionResult(result); break;
        }
    }

    public void ShowNotice(string text) => Notice?.Invoke(text);
    public void Dispose() => Dps.Dispose();
}
