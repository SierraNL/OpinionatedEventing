using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Sagas.Tests.TestSupport;
using Xunit;

namespace OpinionatedEventing.Sagas.Tests;

public sealed class SagaRegistrationTests
{
    [Fact]
    public void AddSaga_populates_registry_with_saga_event_types()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSaga<OrderSaga>();

        var registry = services
            .FirstOrDefault(d => d.ImplementationInstance is MessageHandlerRegistry)
            ?.ImplementationInstance as MessageHandlerRegistry;

        Assert.NotNull(registry);
        Assert.Contains(typeof(OrderPlaced), registry.EventTypes);
        Assert.Contains(typeof(PaymentReceived), registry.EventTypes);
        Assert.Contains(typeof(PaymentFailed), registry.EventTypes);
    }

    [Fact]
    public void AddSagaParticipant_populates_registry_with_participant_event_type()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSagaParticipant<StockParticipant>();

        var registry = services
            .FirstOrDefault(d => d.ImplementationInstance is MessageHandlerRegistry)
            ?.ImplementationInstance as MessageHandlerRegistry;

        Assert.NotNull(registry);
        Assert.Contains(typeof(StockReserved), registry.EventTypes);
    }

    [Fact]
    public void AddSaga_without_prior_AddOpinionatedEventing_does_not_throw()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventingSagas();

        var ex = Record.Exception(() => services.AddSaga<OrderSaga>());

        Assert.Null(ex);
    }

    [Fact]
    public void AddSaga_registers_IEventHandler_adapters_for_each_event_type()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSaga<OrderSaga>();

        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<OrderPlaced>)
            && d.ImplementationType == typeof(SagaEventHandlerAdapter<OrderPlaced>));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<PaymentReceived>)
            && d.ImplementationType == typeof(SagaEventHandlerAdapter<PaymentReceived>));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<PaymentFailed>)
            && d.ImplementationType == typeof(SagaEventHandlerAdapter<PaymentFailed>));
    }

    [Fact]
    public void AddSagaParticipant_registers_IEventHandler_adapter_for_event_type()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSagaParticipant<StockParticipant>();

        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<StockReserved>)
            && d.ImplementationType == typeof(SagaEventHandlerAdapter<StockReserved>));
    }

    [Fact]
    public void AddSaga_called_twice_for_sagas_sharing_event_type_registers_adapter_only_once()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSaga<OrderSaga>();
        services.AddSaga<TimedOrderSaga>(); // also handles OrderPlaced

        var adapters = services.Where(d =>
            d.ServiceType == typeof(IEventHandler<OrderPlaced>)
            && d.ImplementationType == typeof(SagaEventHandlerAdapter<OrderPlaced>)).ToList();

        Assert.Single(adapters);
    }

    [Fact]
    public void AddSaga_with_di_constructor_does_not_register_IEventHandler_adapters()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSaga<DependencyRequiringSaga>();

        Assert.DoesNotContain(services, d =>
            d.ImplementationType?.IsGenericType == true
            && d.ImplementationType.GetGenericTypeDefinition() == typeof(SagaEventHandlerAdapter<>));
    }

    [Fact]
    public void AddSaga_with_di_constructor_silently_skips_event_type_registration()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSaga<DependencyRequiringSaga>();

        var registry = services
            .FirstOrDefault(d => d.ImplementationInstance is MessageHandlerRegistry)
            ?.ImplementationInstance as MessageHandlerRegistry;

        Assert.NotNull(registry);
        Assert.Empty(registry.EventTypes);
    }

    // ---- test fakes ----

    public sealed class DependencyRequiringSagaState { }

    public sealed class DependencyRequiringSagaEvent : IEvent
    {
        public Guid CorrelationId { get; init; }
    }

    // No parameterless constructor — Activator.CreateInstance<> throws MissingMethodException,
    // which GetHandledEventTypes() catches and returns [] silently.
    public sealed class DependencyRequiringSaga : SagaOrchestrator<DependencyRequiringSagaState>
    {
        public DependencyRequiringSaga(string dep) { _ = dep; }

        protected override void Configure(ISagaBuilder<DependencyRequiringSagaState> builder)
            => builder.StartWith<DependencyRequiringSagaEvent>((_, _, _) => Task.CompletedTask);
    }
}
