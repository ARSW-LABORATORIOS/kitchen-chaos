namespace KitchenChaos.Server.Models;

public enum PlateState
{
    Available,
    Ready,
    Dirty
}

public class Plate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public PlateState State { get; set; } = PlateState.Available;
    public string? DishName { get; set; }
    public List<string> Ingredients { get; set; } = new();
}