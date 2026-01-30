/* =============================================================
   OnlineContract – STRESS TEST BULK DATA SEED
   1000+ Users, 100+ Products, 1,000,000 Contracts
   Password for all users: Passw0rd
   ============================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

USE [OnlineContract];
GO

-- Number table for bulk generation
IF OBJECT_ID('tempdb..#Numbers') IS NOT NULL DROP TABLE #Numbers;
CREATE TABLE #Numbers (n INT PRIMARY KEY);
;WITH E1(N) AS (SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 
               UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1),
      E2(N) AS (SELECT 1 FROM E1 a, E1 b),
      E4(N) AS (SELECT 1 FROM E2 a, E2 b),
      E6(N) AS (SELECT 1 FROM E4 a, E2 b)
INSERT #Numbers SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM E6;

DECLARE @passwordHash NVARCHAR(255) = N'jw4vduIrQ+KFUYmHfn3B4efZjCJsldskfNHVR5KDNKk=';
DECLARE @now DATETIME2(0) = dbo.GetLocalTime();
DECLARE @batchSize INT = 50000;
DECLARE @i INT;

PRINT '=== KREIRANJE 1000 KORISNIKA (CUSTOMERS) ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

DECLARE @firstNames TABLE (name NVARCHAR(50), rn INT IDENTITY);
INSERT @firstNames (name) VALUES 
(N'Marko'),(N'Ana'),(N'Nikola'),(N'Milica'),(N'Stefan'),(N'Jovana'),(N'Aleksandar'),(N'Teodora'),(N'Lazar'),(N'Dragan'),
(N'Mina'),(N'Dušan'),(N'Sara'),(N'Ivan'),(N'Jelena'),(N'Petar'),(N'Maja'),(N'Vuk'),(N'Tamara'),(N'Filip'),
(N'Katarina'),(N'Nemanja'),(N'Ivana'),(N'Miloš'),(N'Sofija'),(N'Uroš'),(N'Kristina'),(N'Danilo'),(N'Milena'),(N'Luka'),
(N'Anđela'),(N'Bojan'),(N'Tijana'),(N'Vladimir'),(N'Nevena'),(N'Dejan'),(N'Sanja'),(N'Zoran'),(N'Aleksandra'),(N'Goran'),
(N'Marina'),(N'Dragutin'),(N'Bojana'),(N'Predrag'),(N'Dragana'),(N'Miroslav'),(N'Vesna'),(N'Branislav'),(N'Snežana'),(N'Darko');

DECLARE @lastNames TABLE (name NVARCHAR(50), rn INT IDENTITY);
INSERT @lastNames (name) VALUES 
(N'Petrović'),(N'Jovanović'),(N'Nikolić'),(N'Marković'),(N'Đorđević'),(N'Stojanović'),(N'Ilić'),(N'Stanković'),(N'Pavlović'),(N'Milošević'),
(N'Popović'),(N'Lazić'),(N'Tomić'),(N'Živković'),(N'Kostić'),(N'Krstić'),(N'Vasić'),(N'Radović'),(N'Savić'),(N'Janković'),
(N'Mitrović'),(N'Ristić'),(N'Kovačević'),(N'Obradović'),(N'Simić'),(N'Bogdanović'),(N'Stefanović'),(N'Todorović'),(N'Filipović'),(N'Golubović');

DECLARE @cities TABLE (city NVARCHAR(50), postal NVARCHAR(10), rn INT IDENTITY);
INSERT @cities (city, postal) VALUES 
(N'Beograd',N'11000'),(N'Novi Sad',N'21000'),(N'Niš',N'18000'),(N'Kragujevac',N'34000'),(N'Subotica',N'24000'),
(N'Zrenjanin',N'23000'),(N'Pančevo',N'26000'),(N'Čačak',N'32000'),(N'Leskovac',N'16000'),(N'Kruševac',N'37000'),
(N'Smederevo',N'11300'),(N'Valjevo',N'14000'),(N'Vranje',N'17500'),(N'Šabac',N'15000'),(N'Užice',N'31000');

INSERT dbo.ax_user (first_name, last_name, code, password, is_active, is_deleted, email, stamp, 
                   phone_number, is_group, owner_id, created_dt, password_dt, city, street_address, 
                   postal_code, role_id, input_user_id, is_temp_password)
SELECT 
    f.name,
    l.name,
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(f.name,N'š',N's'),N'đ',N'dj'),N'č',N'c'),N'ć',N'c'),N'ž',N'z')) + '.' +
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(l.name,N'š',N's'),N'đ',N'dj'),N'č',N'c'),N'ć',N'c'),N'ž',N'z')) + 
    CAST(n.n AS NVARCHAR(10)),
    @passwordHash, 1, 0,
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(f.name,N'š',N's'),N'đ',N'dj'),N'č',N'c'),N'ć',N'c'),N'ž',N'z')) + '.' +
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(l.name,N'š',N's'),N'đ',N'dj'),N'č',N'c'),N'ć',N'c'),N'ž',N'z')) + 
    CAST(n.n AS NVARCHAR(10)) + N'@test.com',
    0, N'+38164' + RIGHT('0000000' + CAST(1000000 + n.n AS NVARCHAR(10)), 7),
    0, 0, @now, @now, c.city, N'Ulica br. ' + CAST(n.n AS NVARCHAR(10)), c.postal, 5, 1, 0
FROM #Numbers n
CROSS APPLY (SELECT name FROM @firstNames WHERE rn = ((n.n % 50) + 1)) f
CROSS APPLY (SELECT name FROM @lastNames WHERE rn = ((n.n % 30) + 1)) l
CROSS APPLY (SELECT city, postal FROM @cities WHERE rn = ((n.n % 15) + 1)) c
WHERE n.n <= 1000
AND NOT EXISTS (
    SELECT 1 FROM dbo.ax_user u 
    WHERE u.code = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(f.name,N'š',N's'),N'đ',N'dj'),N'č',N'c'),N'ć',N'c'),N'ž',N'z')) + '.' +
                   LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(l.name,N'š',N's'),N'đ',N'dj'),N'č',N'c'),N'ć',N'c'),N'ž',N'z')) + 
                   CAST(n.n AS NVARCHAR(10))
);

PRINT 'Users created: ' + CAST(@@ROWCOUNT AS VARCHAR);

PRINT '=== KREIRANJE 100 PROIZVODA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

DECLARE @prodTypes TABLE (prefix NVARCHAR(50), rn INT IDENTITY);
INSERT @prodTypes (prefix) VALUES 
(N'Dečja zimska jakna'),(N'Dečji džemper'),(N'Dečje čizme'),(N'Dečja trenerka'),(N'Dečji šal'),
(N'Dečja košulja'),(N'Dečji bermude'),(N'Dečje sandale'),(N'Dečji kaput'),(N'Dečja bluza'),
(N'Dečje helanke'),(N'Dečji sako'),(N'Dečja vesta'),(N'Dečje papuče'),(N'Dečji bodić'),
(N'Dečje pantalone'),(N'Dečja haljina'),(N'Dečji prsluk'),(N'Dečja suknja'),(N'Dečji šorts');

DECLARE @prodSuffixes TABLE (suffix NVARCHAR(50), rn INT IDENTITY);
INSERT @prodSuffixes (suffix) VALUES 
(N'Arctic'),(N'Cozy'),(N'Snow'),(N'Sport'),(N'Classic'),(N'Fancy'),(N'Premium'),(N'Basic'),(N'Pro'),(N'Elite'),
(N'Ultra'),(N'Max'),(N'Plus'),(N'Lite'),(N'Mini'),(N'Maxi'),(N'Deluxe'),(N'Standard'),(N'Gold'),(N'Silver');

INSERT dbo.product (name, input_dt, input_user_id, last_modified_by_id, last_updated_dt, is_active, is_deleted, stamp)
SELECT pt.prefix + N' - ' + ps.suffix + N' V' + CAST(n.n AS NVARCHAR(10)), @now, 1, 1, @now, 1, 0, 0
FROM #Numbers n
CROSS APPLY (SELECT prefix FROM @prodTypes WHERE rn = ((n.n % 20) + 1)) pt
CROSS APPLY (SELECT suffix FROM @prodSuffixes WHERE rn = ((n.n % 20) + 1)) ps
WHERE n.n <= 100
AND NOT EXISTS (SELECT 1 FROM dbo.product p WHERE p.name = pt.prefix + N' - ' + ps.suffix + N' V' + CAST(n.n AS NVARCHAR(10)));

PRINT 'Products created: ' + CAST(@@ROWCOUNT AS VARCHAR);

PRINT '=== KREIRANJE VARIJANTI ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

DECLARE @sizes TABLE (size NVARCHAR(10));
INSERT @sizes VALUES (N'2'),(N'4'),(N'6'),(N'8'),(N'10'),(N'12'),(N'14'),(N'16');

DECLARE @colors TABLE (color NVARCHAR(30));
INSERT @colors VALUES (N'Crvena'),(N'Plava'),(N'Zelena'),(N'Crna'),(N'Bela'),(N'Siva'),(N'Žuta'),(N'Narandžasta'),(N'Ljubičasta'),(N'Roze');

INSERT dbo.product_variant (product_id, size, color, amount, input_dt, input_user_id, last_modified_by_id, last_updated_dt, is_active, is_deleted, stamp)
SELECT p.product_id, s.size, c.color, CAST(500 + (ABS(CHECKSUM(NEWID())) % 9500) AS DECIMAL(18,2)), @now, 1, 1, @now, 1, 0, 0
FROM dbo.product p CROSS JOIN @sizes s CROSS JOIN @colors c
WHERE p.product_id > 0
AND NOT EXISTS (SELECT 1 FROM dbo.product_variant pv WHERE pv.product_id = p.product_id AND pv.size = s.size AND pv.color = c.color);

PRINT 'Variants created: ' + CAST(@@ROWCOUNT AS VARCHAR);

PRINT '=== KREIRANJE INVENTARA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

INSERT dbo.product_inventory (product_variant_id, store_id, qty_on_hand, input_dt, input_user_id, last_modified_by_id, last_updated_dt, is_active, is_deleted, stamp)
SELECT pv.product_variant_id, s.store_id, 5 + (ABS(CHECKSUM(NEWID())) % 95), @now, 1, 1, @now, 1, 0, 0
FROM dbo.product_variant pv CROSS JOIN dbo.store s
WHERE pv.product_variant_id > 0 AND s.store_id > 0
AND NOT EXISTS (SELECT 1 FROM dbo.product_inventory pi WHERE pi.product_variant_id = pv.product_variant_id AND pi.store_id = s.store_id);

PRINT 'Inventory created: ' + CAST(@@ROWCOUNT AS VARCHAR);

PRINT '=== KREIRANJE 1,000,000 UGOVORA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

-- Cache customer and worker IDs
IF OBJECT_ID('tempdb..#Customers') IS NOT NULL DROP TABLE #Customers;
SELECT ax_user_id as id, ROW_NUMBER() OVER (ORDER BY ax_user_id) as rn INTO #Customers FROM ax_user WHERE role_id = 5 AND is_active = 1 AND is_deleted = 0;
DECLARE @custCount INT = (SELECT COUNT(*) FROM #Customers);

IF OBJECT_ID('tempdb..#Workers') IS NOT NULL DROP TABLE #Workers;
SELECT ax_user_id as id, ROW_NUMBER() OVER (ORDER BY ax_user_id) as rn INTO #Workers FROM ax_user WHERE role_id IN (6,7,8) AND is_active = 1 AND is_deleted = 0;
DECLARE @workerCount INT = (SELECT COUNT(*) FROM #Workers);

DECLARE @defaultDt DATETIME2(0) = '1900-01-01 00:00:00';
DECLARE @totalContracts INT = 1000000;
DECLARE @batch INT = 1;
DECLARE @totalBatches INT = @totalContracts / @batchSize;

WHILE @batch <= @totalBatches
BEGIN
    PRINT 'Batch ' + CAST(@batch AS VARCHAR) + '/' + CAST(@totalBatches AS VARCHAR) + ' - ' + CONVERT(VARCHAR, GETDATE(), 120);
    
    INSERT dbo.contract (input_dt, input_user_id, contract_state, last_modified_by_id, last_updated_dt, stamp, is_active, is_deleted,
                         delivered_dt, written_off_dt, rejected_dt, cancelled_dt, amount, amt_matched)
    SELECT 
        DATEADD(SECOND, n.n + ((@batch - 1) * @batchSize), '2026-01-01'),
        c.id,
        CASE 
            WHEN n.n % 10 IN (0,1,2,3) THEN 17  -- 40% Delivered
            WHEN n.n % 10 IN (4,5) THEN 15      -- 20% Completed
            WHEN n.n % 10 = 6 THEN 14           -- 10% In Progress
            WHEN n.n % 10 = 7 THEN 19           -- 10% Cancelled
            WHEN n.n % 10 = 8 THEN 20           -- 10% Written Off
            ELSE 21                              -- 10% Refunded
        END,
        w.id,
        DATEADD(DAY, 2, DATEADD(SECOND, n.n + ((@batch - 1) * @batchSize), '2026-01-01')),
        0, 1, 0,
        CASE WHEN n.n % 10 IN (0,1,2,3,9) THEN DATEADD(DAY, 3, DATEADD(SECOND, n.n + ((@batch - 1) * @batchSize), '2026-01-01')) ELSE @defaultDt END,
        CASE WHEN n.n % 10 = 8 THEN DATEADD(DAY, 5, DATEADD(SECOND, n.n + ((@batch - 1) * @batchSize), '2026-01-01')) ELSE @defaultDt END,
        @defaultDt,
        CASE WHEN n.n % 10 = 7 THEN DATEADD(DAY, 1, DATEADD(SECOND, n.n + ((@batch - 1) * @batchSize), '2026-01-01')) ELSE @defaultDt END,
        CAST(1000 + (n.n % 49000) AS DECIMAL(18,2)),
        CASE WHEN n.n % 10 IN (0,1,2,3,4,5,6,9) THEN CAST(1000 + (n.n % 49000) AS DECIMAL(18,2)) ELSE 0 END
    FROM #Numbers n
    JOIN #Customers c ON c.rn = ((n.n % @custCount) + 1)
    JOIN #Workers w ON w.rn = ((n.n % @workerCount) + 1)
    WHERE n.n <= @batchSize;
    
    SET @batch = @batch + 1;
END

DECLARE @contractTotal INT = (SELECT COUNT(*) FROM contract WHERE contract_id > 0);
PRINT 'Contracts created. Total: ' + CAST(@contractTotal AS VARCHAR);

PRINT '=== KREIRANJE STAVKI UGOVORA (1-3 po ugovoru) ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

-- Cache variants
IF OBJECT_ID('tempdb..#Variants') IS NOT NULL DROP TABLE #Variants;
SELECT pv.product_variant_id, p.name as product_name, pv.size, pv.color, pv.amount, 
       ROW_NUMBER() OVER (ORDER BY pv.product_variant_id) as rn
INTO #Variants FROM product_variant pv JOIN product p ON p.product_id = pv.product_id WHERE pv.product_variant_id > 0;
DECLARE @varCount INT = (SELECT COUNT(*) FROM #Variants);

-- Insert contract details in batches
SET @batch = 1;
DECLARE @contractBatchSize INT = 100000;
DECLARE @maxContractId INT = (SELECT MAX(contract_id) FROM contract);
DECLARE @minContractId INT = (SELECT MIN(contract_id) FROM contract WHERE contract_id > 0);
DECLARE @currentMin INT = @minContractId;

WHILE @currentMin <= @maxContractId
BEGIN
    PRINT 'Contract details batch from ' + CAST(@currentMin AS VARCHAR) + ' - ' + CONVERT(VARCHAR, GETDATE(), 120);
    
    -- First item for each contract
    INSERT dbo.contract_det (contract_id, product_variant_id, quantity, amount, product_name, size, color,
                             item_state_id, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp, is_active, is_deleted)
    SELECT c.contract_id, v.product_variant_id, 1 + (c.contract_id % 5), v.amount, v.product_name, v.size, v.color, 23,
           c.input_dt, c.input_user_id, c.last_modified_by_id, c.last_updated_dt, 0, 1, 0
    FROM contract c
    JOIN #Variants v ON v.rn = ((c.contract_id % @varCount) + 1)
    WHERE c.contract_id >= @currentMin AND c.contract_id < @currentMin + @contractBatchSize
    AND NOT EXISTS (SELECT 1 FROM contract_det cd WHERE cd.contract_id = c.contract_id);
    
    -- Second item for ~50% of contracts
    INSERT dbo.contract_det (contract_id, product_variant_id, quantity, amount, product_name, size, color,
                             item_state_id, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp, is_active, is_deleted)
    SELECT c.contract_id, v.product_variant_id, 1 + (c.contract_id % 3), v.amount, v.product_name, v.size, v.color, 23,
           c.input_dt, c.input_user_id, c.last_modified_by_id, c.last_updated_dt, 0, 1, 0
    FROM contract c
    JOIN #Variants v ON v.rn = (((c.contract_id + 1000) % @varCount) + 1)
    WHERE c.contract_id >= @currentMin AND c.contract_id < @currentMin + @contractBatchSize
    AND c.contract_id % 2 = 0
    AND NOT EXISTS (SELECT 1 FROM contract_det cd WHERE cd.contract_id = c.contract_id AND cd.product_variant_id = v.product_variant_id);
    
    -- Third item for ~25% of contracts
    INSERT dbo.contract_det (contract_id, product_variant_id, quantity, amount, product_name, size, color,
                             item_state_id, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp, is_active, is_deleted)
    SELECT c.contract_id, v.product_variant_id, 1 + (c.contract_id % 2), v.amount, v.product_name, v.size, v.color, 23,
           c.input_dt, c.input_user_id, c.last_modified_by_id, c.last_updated_dt, 0, 1, 0
    FROM contract c
    JOIN #Variants v ON v.rn = (((c.contract_id + 5000) % @varCount) + 1)
    WHERE c.contract_id >= @currentMin AND c.contract_id < @currentMin + @contractBatchSize
    AND c.contract_id % 4 = 0
    AND NOT EXISTS (SELECT 1 FROM contract_det cd WHERE cd.contract_id = c.contract_id AND cd.product_variant_id = v.product_variant_id);
    
    SET @currentMin = @currentMin + @contractBatchSize;
END

DECLARE @detTotal INT = (SELECT COUNT(*) FROM contract_det);
PRINT 'Contract details created: ' + CAST(@detTotal AS VARCHAR);

PRINT '=== AŽURIRANJE IZNOSA UGOVORA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

UPDATE c SET 
    amount = ISNULL(d.total, 0), 
    amt_matched = CASE WHEN c.contract_state IN (17, 15, 14, 16, 21) THEN ISNULL(d.total, 0) ELSE 0 END
FROM contract c 
LEFT JOIN (SELECT contract_id, SUM(quantity * amount) as total FROM contract_det WHERE is_deleted = 0 GROUP BY contract_id) d 
ON d.contract_id = c.contract_id
WHERE c.contract_id > 0;

PRINT '=== KREIRANJE NAPOMENA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

-- Notes for ~10% of contracts
INSERT dbo.note (contract_id, product_id, subject, is_main, input_user_id, last_modified_by_id, input_dt, last_updated_dt, stamp, is_deleted, comment, is_active)
SELECT c.contract_id, NULL,
    CASE (c.contract_id % 8)
        WHEN 0 THEN N'Uspešna dostava' WHEN 1 THEN N'Hitna isporuka' WHEN 2 THEN N'Kontakt kupca'
        WHEN 3 THEN N'Napomena o pakovanju' WHEN 4 THEN N'Problem sa adresom' WHEN 5 THEN N'Povratni poziv'
        WHEN 6 THEN N'Specijalni zahtev' ELSE N'Interna napomena'
    END, 1, c.last_modified_by_id, c.last_modified_by_id, c.last_updated_dt, c.last_updated_dt, 0, 0,
    CASE (c.contract_id % 8)
        WHEN 0 THEN N'Paket uspešno dostavljen kupcu.'
        WHEN 1 THEN N'Hitna isporuka - prioritet!'
        WHEN 2 THEN N'Kontaktirati kupca pre dostave.'
        WHEN 3 THEN N'Poklon pakovanje.'
        WHEN 4 THEN N'Proveriti adresu.'
        WHEN 5 THEN N'Povratni poziv pre dostave.'
        WHEN 6 THEN N'Specijalni zahtevi.'
        ELSE N'Standardna obrada.'
    END, 1
FROM contract c 
WHERE c.contract_id > 0 AND c.contract_id % 10 = 0
AND NOT EXISTS (SELECT 1 FROM note n WHERE n.contract_id = c.contract_id);

PRINT 'Notes created: ' + CAST(@@ROWCOUNT AS VARCHAR);

PRINT '=== KREIRANJE TASKOVA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

-- Tasks for written-off and refunded contracts
INSERT dbo.task (subject, comments, input_dt, assigned_to_user_id, initiated_by_user_id, priority, status, reminder_dt, contract_id, completed_dt, stamp)
SELECT 
    CASE WHEN c.contract_state = 20 THEN N'Write-Off' ELSE N'Refund' END,
    N'Approval #' + CAST(c.contract_id AS NVARCHAR(10)),
    c.last_updated_dt, 3, 2, 28, 32, DATEADD(DAY, 1, c.last_updated_dt), c.contract_id, DATEADD(HOUR, 2, c.last_updated_dt), 0
FROM contract c 
WHERE c.contract_state IN (20, 21) AND c.contract_id > 0
AND NOT EXISTS (SELECT 1 FROM task t WHERE t.contract_id = c.contract_id);

PRINT 'Tasks created: ' + CAST(@@ROWCOUNT AS VARCHAR);

PRINT '=== KREIRANJE PLAĆANJA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

-- Payments in batches
SET @currentMin = @minContractId;
WHILE @currentMin <= @maxContractId
BEGIN
    INSERT dbo.payment (contract_id, provider, external_order_id, amt_gross, currency, status, created_dt, updated_dt, stamp, transaction_id)
    SELECT c.contract_id, N'WSPay', N'OC-' + CAST(c.contract_id AS NVARCHAR(10)), c.amount, N'RSD',
           CASE WHEN c.contract_state = 21 THEN N'Refunded' WHEN c.contract_state IN (19, 20) THEN N'Cancelled' ELSE N'Completed' END,
           c.input_dt, c.last_updated_dt, 0, N'TXN-' + CAST(c.contract_id AS NVARCHAR(10))
    FROM contract c 
    WHERE c.contract_id >= @currentMin AND c.contract_id < @currentMin + @contractBatchSize
    AND c.contract_id > 0 AND c.amount > 0
    AND NOT EXISTS (SELECT 1 FROM payment p WHERE p.contract_id = c.contract_id);
    
    SET @currentMin = @currentMin + @contractBatchSize;
END

PRINT '=== KREIRANJE REVIEW-OVA ===';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

IF NOT EXISTS (SELECT 1 FROM dbo.review)
BEGIN
    DECLARE @nowReview DATETIME2 = dbo.GetLocalTime();

    INSERT INTO dbo.review (input_dt, input_user_id, comment, mark, is_active, is_deleted, stamp)
    VALUES
    (DATEADD(DAY, -10, @nowReview), 0, N'Great quality clothes for my kids! Very soft fabric and vibrant colors. Will definitely order again.', 5, 1, 0, 0),
    (DATEADD(DAY, -8, @nowReview), 0, N'Fast delivery and good packaging. The sizes run a bit small though.', 4, 1, 0, 0),
    (DATEADD(DAY, -6, @nowReview), 0, N'My daughter loves the dresses! Perfect for school.', 5, 1, 0, 0),
    (DATEADD(DAY, -5, @nowReview), 0, NULL, 4, 1, 0, 0),
    (DATEADD(DAY, -3, @nowReview), 0, N'Good value for money. Reasonable prices for the quality.', 4, 1, 0, 0),
    (DATEADD(DAY, -7, @nowReview), 1, N'Excellent service! The staff was very helpful when I had questions about sizing.', 5, 1, 0, 0),
    (DATEADD(DAY, -4, @nowReview), 1, N'Nice selection of trendy clothes for teens. My son approved!', 4, 1, 0, 0),
    (DATEADD(DAY, -2, @nowReview), 0, N'The baby onesies are super cute and comfortable. Easy to wash too.', 5, 1, 0, 0),
    (DATEADD(DAY, -1, @nowReview), 0, N'Decent products but shipping took longer than expected.', 3, 1, 0, 0),
    (DATEADD(HOUR, -12, @nowReview), 0, N'Love the organic cotton options! Safe for sensitive skin.', 5, 1, 0, 0);

    PRINT 'Reviews created: ' + CAST(@@ROWCOUNT AS VARCHAR);
END

-- Cleanup
DROP TABLE #Numbers;
DROP TABLE #Customers;
DROP TABLE #Workers;
DROP TABLE #Variants;

PRINT '';
PRINT '=========================================================';
PRINT '  STRESS TEST BULK DATA COMPLETED!';
PRINT '=========================================================';
PRINT CONVERT(VARCHAR, GETDATE(), 120);

SELECT 'Users' as Entity, COUNT(*) as Total FROM ax_user WHERE ax_user_id > 0
UNION ALL SELECT 'Products', COUNT(*) FROM product WHERE product_id > 0
UNION ALL SELECT 'Variants', COUNT(*) FROM product_variant
UNION ALL SELECT 'Inventory', COUNT(*) FROM product_inventory
UNION ALL SELECT 'Contracts', COUNT(*) FROM contract WHERE contract_id > 0
UNION ALL SELECT 'ContractDet', COUNT(*) FROM contract_det
UNION ALL SELECT 'Notes', COUNT(*) FROM note
UNION ALL SELECT 'Payments', COUNT(*) FROM payment
UNION ALL SELECT 'Tasks', COUNT(*) FROM task
UNION ALL SELECT 'Reviews', COUNT(*) FROM review;

PRINT '';
PRINT '  Password za sve korisnike: Passw0rd';
PRINT '';
GO
