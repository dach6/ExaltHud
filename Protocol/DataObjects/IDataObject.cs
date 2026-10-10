using System;

namespace ExaltHUD.Protocol.DataObjects;

internal interface IDataObject : ICloneable
{
	IDataObject Read(PacketReader reader);

	void Write(PacketWriter writer);
}
