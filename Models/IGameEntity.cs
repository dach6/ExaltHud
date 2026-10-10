namespace ExaltHUD.Models;

internal interface IGameEntity<TId>
{
	string Name { get; }

	TId ID { get; }
}
