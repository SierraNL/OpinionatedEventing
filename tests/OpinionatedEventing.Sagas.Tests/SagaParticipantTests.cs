using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.Sagas.Tests.TestSupport;
using Xunit;

namespace OpinionatedEventing.Sagas.Tests;

public sealed class SagaParticipantTests
{
    [Fact]
    public async Task Participant_is_invoked_when_matching_event_dispatched()
    {
        var participant = new StockParticipant();
        await using var h = SagaTestHarness.Create(s =>
        {
            s.AddSingleton(participant);
            s.AddSagaParticipant<StockParticipant>();
        });
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(
            new StockReserved { OrderId = orderId, CorrelationId = Guid.NewGuid() }, ct);

        Assert.Single(participant.Handled);
        Assert.Equal(orderId, participant.Handled[0].OrderId);
    }

    [Fact]
    public async Task Participant_sends_command_via_context()
    {
        var participant = new StockParticipant();
        await using var h = SagaTestHarness.Create(s =>
        {
            s.AddSingleton(participant);
            s.AddSagaParticipant<StockParticipant>();
        });
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(
            new StockReserved { OrderId = orderId, CorrelationId = Guid.NewGuid() }, ct);

        var cmd = Assert.Single(h.Publisher.SentCommands.OfType<ReserveStock>());
        Assert.Equal(orderId, cmd.OrderId);
    }

    [Fact]
    public async Task Participant_not_invoked_for_unrelated_event()
    {
        var participant = new StockParticipant();
        await using var h = SagaTestHarness.Create(s =>
        {
            s.AddSingleton(participant);
            s.AddSagaParticipant<StockParticipant>();
        });
        var ct = TestContext.Current.CancellationToken;

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = Guid.NewGuid() }, ct);

        Assert.Empty(participant.Handled);
    }
}
