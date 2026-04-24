Feature: Saga Orchestration
  As a developer using OpinionatedEventing sagas
  I want the saga engine to create, route, and complete saga instances correctly
  So that distributed workflows are coordinated reliably

  Scenario: Initiating event creates a new saga instance
    Given the order saga is registered
    When an OrderPlaced event is dispatched
    Then a saga instance exists in the store
    And the saga status is Active

  Scenario: Subsequent event routes to the existing saga and updates its state
    Given the order saga is registered
    And an OrderPlaced event has been dispatched
    When a PaymentReceived event is dispatched
    Then the saga instance state shows payment was processed
    And the saga status is Completed

  Scenario: Compensation event invokes the CompensateWith handler
    Given the order saga is registered
    And an OrderPlaced event has been dispatched
    When a PaymentFailed event is dispatched
    Then the saga status is Completed
    And the saga instance state shows payment was not processed

  Scenario: Choreography participant reacts to an event and sends a command
    Given the notification participant is registered
    When an OrderShipped event is dispatched
    Then the notification participant command was published

  Scenario: Saga with a timeout handler fires when an expired saga is processed
    Given the order saga with timeout is registered
    When an OrderPlaced event is dispatched
    And the saga timeout worker processes expired sagas
    Then the saga timeout handler was invoked

  Scenario: Saga with custom correlation routes events by the custom key
    Given the order saga with custom correlation is registered
    When an OrderPlaced event is dispatched with custom correlation key "custom-key-1"
    And a PaymentReceived event is dispatched with custom correlation key "custom-key-1"
    Then the saga instance state shows payment was processed
