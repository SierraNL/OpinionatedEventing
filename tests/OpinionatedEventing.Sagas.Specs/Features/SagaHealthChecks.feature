Feature: OpinionatedEventing.Sagas health checks

  Scenario: Saga timeout backlog health check is healthy when expired sagas are below threshold
    Given there are 3 expired sagas
    And the saga timeout backlog threshold is 10
    When the saga timeout backlog health check runs
    Then the result is Healthy

  Scenario: Saga timeout backlog health check degrades when expired sagas exceed threshold
    Given there are 15 expired sagas
    And the saga timeout backlog threshold is 10
    When the saga timeout backlog health check runs
    Then the result is Degraded

  Scenario: Saga timeout backlog health check is healthy when no ISagaStateStore is registered
    Given no ISagaStateStore is registered
    When the saga timeout backlog health check runs
    Then the result is Healthy
