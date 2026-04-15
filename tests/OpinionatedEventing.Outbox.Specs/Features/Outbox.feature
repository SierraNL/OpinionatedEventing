Feature: Outbox
  As an application developer
  I want outbox messages to be dispatched to the transport reliably
  So that message delivery is decoupled from business operations

  Scenario: Publisher saves an event to the outbox
    Given a messaging context with a known correlation ID
    When an event is published via IPublisher
    Then one outbox message with kind "Event" is saved to the store
    And the outbox message carries the correlation ID

  Scenario: Publisher saves a command to the outbox
    Given a messaging context with a known correlation ID
    When a command is sent via IPublisher
    Then one outbox message with kind "Command" is saved to the store

  Scenario: Dispatcher forwards a pending message to the transport
    Given a pending outbox message exists in the store
    When the dispatcher worker processes the batch
    Then the message is forwarded to the transport
    And the message is marked as processed in the store

  Scenario: Dispatcher increments attempt count on transient failure
    Given a pending outbox message with 0 failed attempts exists in the store
    And the transport will fail on the next attempt
    And the max attempts is configured to 5
    When the dispatcher worker processes the batch
    Then the message attempt count is 1
    And the message is not dead-lettered

  Scenario: Dispatcher dead-letters a message after max attempts are exhausted
    Given a pending outbox message with 4 failed attempts exists in the store
    And the transport always fails
    And the max attempts is configured to 5
    When the dispatcher worker processes the batch
    Then the message is dead-lettered
    And the message is not marked as processed in the store

  Scenario: Transaction guard prevents publishing outside a transaction
    Given a transaction guard that always rejects
    When an event is published via IPublisher
    Then an InvalidOperationException is raised
