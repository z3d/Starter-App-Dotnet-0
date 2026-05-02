ALTER TABLE Products
ADD RowVersion rowversion NOT NULL;

ALTER TABLE Orders
ADD RowVersion rowversion NOT NULL;
