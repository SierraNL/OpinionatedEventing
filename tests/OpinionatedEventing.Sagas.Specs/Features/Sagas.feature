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
