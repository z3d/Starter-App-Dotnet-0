ALTER TABLE Products ADD CONSTRAINT CK_Products_Stock_NonNegative CHECK (Stock >= 0);
