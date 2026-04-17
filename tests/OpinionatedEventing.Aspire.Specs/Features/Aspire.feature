Feature: Aspire — OpinionatedEventing.Aspire health checks

  Scenario: Outbox backlog health check is healthy when pending messages are below threshold
    Given the outbox has 50 pending messages
    And the outbox backlog threshold is 100
    When the outbox backlog health check runs
    Then the result is Healthy

  Scenario: Outbox backlog health check degrades when pending messages exceed threshold
    Given the outbox has 150 pending messages
    And the outbox backlog threshold is 100
    When the outbox backlog health check runs
    Then the result is Degraded

  Scenario: Outbox backlog health check is healthy when no IOutboxMonitor is registered
    Given no IOutboxMonitor is registered
    When the outbox backlog health check runs
    Then the result is Healthy

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

  Scenario: Broker connectivity health check is healthy when no broker transport is registered
    Given no broker transport is registered
    When the broker connectivity health check runs
    Then the result is Healthy
