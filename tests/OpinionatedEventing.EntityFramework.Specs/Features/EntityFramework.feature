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
