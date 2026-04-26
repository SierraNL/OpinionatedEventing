Feature: DDD Aggregate Support and Messaging Context
  As a developer using OpinionatedEventing
  I want aggregate roots to collect domain events in memory
  And I want the handler runner to initialise IMessagingContext from the inbound message envelope
  So that correlation context flows automatically through the entire message chain

  Scenario: Aggregate collects domain events in order
    Given an aggregate root
    When two domain events are raised
    Then the DomainEvents collection contains both events in order

  Scenario: Domain events are cleared after ClearDomainEvents is called
    Given an aggregate root with one domain event raised
    When ClearDomainEvents is called
    Then the DomainEvents collection is empty

  Scenario: Handler runner initialises correlation context before the handler runs
    Given a registered event handler that captures IMessagingContext
    When the handler runner dispatches an event with a known CorrelationId and CausationId
    Then the captured CorrelationId matches the dispatched CorrelationId
    And the captured CausationId matches the dispatched CausationId

  Scenario: Handler runner dispatches to all registered event handlers
    Given two registered event handlers for the same event type
    When the handler runner dispatches a matching event
    Then both handlers are invoked

  Scenario: Handler runner dispatches to the registered command handler
    Given a registered command handler that captures the command
    When the handler runner dispatches a command
    Then the command handler is invoked with the correct payload
