-- Migrate outbox message Type values from CLR type names to stable versioned contracts.
-- This ensures unprocessed rows route correctly after the code switch.

UPDATE OutboxMessages
SET Type = 'order.created.v1'
WHERE Type = 'OrderCreatedDomainEvent';

UPDATE OutboxMessages
SET Type = 'order.status-changed.v1'
WHERE Type = 'OrderStatusChangedDomainEvent';
