namespace ExaltHUD.Models;

internal class MapTile
{
	public TileStructure TileStructure;

	public short GroundType;

	public short X;

	public ushort Y;

	public MapTile(TileStructure tileDefinition, short x, short y, ushort tileType)
	{
		TileStructure = tileDefinition;
		GroundType = x;
		X = y;
		Y = tileType;
	}
}
