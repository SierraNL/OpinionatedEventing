Feature: AzureServiceBus Transport
  As a developer using OpinionatedEventing
  I want the Azure Service Bus transport to route events and commands correctly
  So that my microservices can communicate reliably

  Scenario: Message naming convention converts PascalCase to kebab-case
    Given an event type named "OrderPlaced"
    When I resolve the topic name
    Then the topic name is "order-placed"

  Scenario: Message naming convention respects explicit MessageTopic attribute
    Given an event type with a MessageTopic attribute set to "my-custom-topic"
    When I resolve the topic name
    Then the topic name is "my-custom-topic"

  Scenario: Message naming convention converts command type to queue name
    Given a command type named "ProcessPayment"
    When I resolve the queue name
    Then the queue name is "process-payment"

  Scenario: Transport registration wires up ITransport
    Given the Azure Service Bus transport is registered with a connection string
    When the service provider is built
    Then ITransport is registered in the container

  Scenario: Transport registration configures ServiceName option
    Given the Azure Service Bus transport is registered with ServiceName "order-service"
    When the service provider is built
    Then the ServiceName option is "order-service"
