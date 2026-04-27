Feature: OpinionatedEventing.Outbox health checks

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
