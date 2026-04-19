Feature: RabbitMQ Transport
  As a developer using OpinionatedEventing with RabbitMQ
  I want events and commands to be routed to the correct exchanges and queues
  So that my microservices can communicate reliably

  Scenario: Event type is mapped to a kebab-case exchange name
    Given an event type named "OrderPlaced"
    When I resolve the exchange name
    Then the exchange name is "order-placed"

  Scenario: MessageTopic attribute overrides the exchange name
    Given an event type with a MessageTopic attribute set to "my-custom-exchange"
    When I resolve the exchange name
    Then the exchange name is "my-custom-exchange"

  Scenario: Command type is mapped to a kebab-case queue name
    Given a command type named "ProcessPayment"
    When I resolve the queue name
    Then the queue name is "process-payment"

  Scenario: MessageQueue attribute overrides the queue name
    Given a command type with a MessageQueue attribute set to "payments"
    When I resolve the queue name
    Then the queue name is "payments"

  Scenario: Event consumer queue name includes the service name prefix
    Given an event type named "OrderPlaced"
    And the service name is "order-service"
    When I resolve the event consumer queue name
    Then the event queue name is "order-service.order-placed"

  Scenario: Transport registration wires up ITransport
    Given the RabbitMQ transport is registered with a connection string
    Then ITransport is registered in the service collection

  Scenario: Transport registration configures the ServiceName option
    Given the RabbitMQ transport is registered with ServiceName "order-service"
    When the service provider is built
    Then the ServiceName option is "order-service"
