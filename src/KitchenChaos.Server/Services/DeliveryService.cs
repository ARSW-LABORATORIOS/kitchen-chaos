using System.Collections.Concurrent;
using KitchenChaos.Server.Models;

namespace KitchenChaos.Server.Services;

public class DeliveryResult
{
    public bool Success { get; set; }
    public bool Correct { get; set; }
    public string? Error { get; set; }
    public int Score { get; set; }
    public Plate? Plate { get; set; }
}

public class DeliveryService
{
    private const int CorrectOrderPoints = 100;
    private const int IncorrectOrderPenalty = 25;

    private readonly OrderService _orderService;
    private readonly PlatingService _platingService;

    private readonly ConcurrentDictionary<string, int> _scoresByRoom = new();

    public DeliveryService(
        OrderService orderService,
        PlatingService platingService)
    {
        _orderService = orderService;
        _platingService = platingService;
    }

    public DeliveryResult Deliver(string roomCode, string plateId)
    {
        var plateResult = _platingService.TakePlateForDelivery(
            roomCode,
            plateId);

        if (!plateResult.Success)
        {
            return new DeliveryResult
            {
                Success = false,
                Error = plateResult.Error
            };
        }

        var plate = plateResult.Plate!;

        var correct = _orderService.ValidateDelivery(
            roomCode,
            new List<string>(plate.Ingredients));

        var score = _scoresByRoom.AddOrUpdate(
            roomCode,
            correct ? CorrectOrderPoints : 0,
            (_, current) => correct
                ? current + CorrectOrderPoints
                : Math.Max(0, current - IncorrectOrderPenalty));

        return new DeliveryResult
        {
            Success = true,
            Correct = correct,
            Score = score,
            Plate = plate
        };
    }

    public int GetScore(string roomCode)
    {
        return _scoresByRoom.TryGetValue(roomCode, out var score)
            ? score
            : 0;
    }
}