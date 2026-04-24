Feature: EntityFramework EF Core integration
  As a developer using OpinionatedEventing with EF Core
  I want domain events to flow to the outbox atomically and messages to be queryable
  So that message delivery is guaranteed

  Scenario: Domain event is harvested and written to the outbox when SaveChanges is called
    Given an order aggregate with a pending domain event
    And a known messaging context correlation ID
    When SaveChangesAsync is called with the DomainEventInterceptor active
    Then one outbox message with kind "Event" is written to the database
    And the outbox message carries the messaging context correlation ID

  Scenario: Domain events are cleared from the aggregate after harvest
    Given an order aggregate with a pending domain event
    When SaveChangesAsync is called with the DomainEventInterceptor active
    Then the aggregate has no remaining domain events

  Scenario: Staged outbox message is returned as pending
    Given a message is staged and committed via EFCoreOutboxStore
    When pending messages are queried
    Then the staged message is in the result

  Scenario: Processed message is excluded from the pending batch
    Given a message is staged and committed via EFCoreOutboxStore
    When the message is marked as processed
    And pending messages are queried
    Then no pending messages are returned

  Scenario: Failed message is excluded from the pending batch
    Given a message is staged and committed via EFCoreOutboxStore
    When the message is marked as failed
    And pending messages are queried
    Then no pending messages are returned

  Scenario: New saga state is saved and can be found by saga type and correlation ID
    Given a new saga state for type "SpecsOrderSaga" with correlation ID "order-123"
    When the saga state is saved via EFCoreSagaStateStore
    Then the saga state can be found by type "SpecsOrderSaga" and correlation ID "order-123"

  Scenario: Saga state update is reflected when found again
    Given a new saga state for type "SpecsOrderSaga" with correlation ID "order-456"
    And the saga state is saved via EFCoreSagaStateStore
    When the saga state status is updated to Completed
    Then the found saga state has status Completed

  Scenario: Unknown saga type and correlation ID returns null
    When a saga state is looked up with type "UnknownSaga" and correlation ID "none"
    Then the found saga state is null

  Scenario: Expired active saga state is included in the expired batch
    Given a new saga state for type "SpecsOrderSaga" with correlation ID "order-789"
    And the saga state expires in the past
    When the saga state is saved via EFCoreSagaStateStore
    Then the expired saga query returns the saga state

  Scenario: Completed saga state is excluded from the expired batch
    Given a new saga state for type "SpecsOrderSaga" with correlation ID "order-completed"
    And the saga state expires in the past
    And the saga state status is Completed
    When the saga state is saved via EFCoreSagaStateStore
    Then the expired saga query returns no results

  Scenario: Synchronous SaveChanges triggers domain event harvest
    Given an order aggregate with a pending domain event
    When SaveChanges is called synchronously with the DomainEventInterceptor active
    Then one outbox message with kind "Event" is written to the database
    And the aggregate has no remaining domain events

  Scenario: IncrementAttemptAsync increments the attempt count on an existing message
    Given a message is staged and committed via EFCoreOutboxStore
    When the message attempt count is incremented with error "transient error"
    Then the message attempt count is 1 and error is "transient error"

  Scenario: MarkProcessedAsync with an unknown ID is a no-op
    When MarkProcessedAsync is called with an unknown message ID
    And pending messages are queried
    Then no pending messages are returned

  Scenario: MarkFailedAsync with an unknown ID is a no-op
    When MarkFailedAsync is called with an unknown message ID
    And pending messages are queried
    Then no pending messages are returned

  Scenario: IncrementAttemptAsync with an unknown ID is a no-op
    When IncrementAttemptAsync is called with an unknown message ID
    And pending messages are queried
    Then no pending messages are returned
