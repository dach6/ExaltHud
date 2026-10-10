using ExaltHUD.Protocol.Packets;
using ExaltHUD.Protocol.DataObjects;

namespace ExaltHUD.Models;

internal class AoeDamageInfo
{
	public readonly WorldPosData Position;

	public readonly ushort Damage;

	public readonly ushort EffectType;

	public readonly float Radius;

	public readonly bool Duration;

	public AoeDamageInfo(AoePacket packet)
	{
		Position = packet._startingPos;
		Damage = packet._angleRaw;
		EffectType = packet.OrigType;
		Radius = packet.Radius;
		Duration = packet._armorPiercing;
	}
}
