namespace ExaltHUD.Models;

public class PotionInfo
{
	public int Type;

	public byte Quantity;

	public PotionInfo(int type, byte quantity)
	{
		Type = type;
		Quantity = quantity;
	}
}
