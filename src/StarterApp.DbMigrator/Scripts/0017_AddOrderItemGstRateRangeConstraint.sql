IF EXISTS (SELECT 1 FROM OrderItems WHERE GstRate < 0 OR GstRate > 1)
    THROW 51017, 'Cannot add CK_OrderItems_GstRate_Range because OrderItems contains GST rates outside the 0..1 range.', 1;

ALTER TABLE OrderItems
    ADD CONSTRAINT CK_OrderItems_GstRate_Range CHECK (GstRate >= 0 AND GstRate <= 1);
