using OnlineContract.Data;
using OnlineContract.Models;
using System.Linq;

namespace OnlineContract.Tests;

public static class TestDataSeeder
{
    public static void Seed(AppDbContext db)
    {
        // Prevent duplicate seeding when factory builds multiple times
        if (db.Stores.Any()) return;
        // Minimal stores
        db.Stores.Add(new Store { StoreId = 1, Name = "Store 1", CreatedDt = DateTime.Now, UpdatedDt = DateTime.Now, LastModifiedUserId = 2 });
        db.Stores.Add(new Store { StoreId = 2, Name = "Store 2", CreatedDt = DateTime.Now, UpdatedDt = DateTime.Now, LastModifiedUserId = 2 });

        // One active user for auth
        db.AxUsers.Add(new AxUser
        {
            Id = 123,
            Code = "testuser",
            Email = "user@example.com",
            RoleId = 5, // Customer
            IsActive = true,
            IsDeleted = false,
            Password = OnlineContract.Helpers.PasswordHelper.HashPassword("Password1"),
            FirstName = "Test",
            LastName = "User",
            CreatedDt = DateTime.Now,
            PasswordDt = DateTime.Now,
            Stamp = 0
        });

        // Product & variants
        var p = new Product { Id = 100, Name = "T-Shirt", IsActive = true, IsDeleted = false, InputDt = DateTime.Now, Stamp = 0 };
        db.Products.Add(p);
        var v1 = new ProductVariant { Id = 101, ProductId = p.Id, Size = "M", Color = "Red", IsActive = true, IsDeleted = false, Amount = 1500m, PhotoFileName = "shirt-red-m.jpg" };
        var v2 = new ProductVariant { Id = 102, ProductId = p.Id, Size = "L", Color = "Blue", IsActive = true, IsDeleted = false, Amount = 1700m, PhotoFileName = "shirt-blue-l.jpg" };
        db.ProductVariants.AddRange(v1, v2);

        // Inventory: v1 has qty, v2 has none
        db.ProductInventories.Add(new ProductInventory { Id = 1001, ProductVariantId = 101, StoreId = 1, QtyOnHand = 5, IsActive = true, IsDeleted = false, InputDt = DateTime.Now, Stamp = 0 });
        db.ProductInventories.Add(new ProductInventory { Id = 1002, ProductVariantId = 101, StoreId = 2, QtyOnHand = 3, IsActive = true, IsDeleted = false, InputDt = DateTime.Now, Stamp = 0 });
        db.ProductInventories.Add(new ProductInventory { Id = 1003, ProductVariantId = 102, StoreId = 1, QtyOnHand = 0, IsActive = true, IsDeleted = false, InputDt = DateTime.Now, Stamp = 0 });

        db.SaveChanges();

        // Contract State lookup mapping (for item_state_id transitions/end-state checks)
        // Mark Rejected as an end state for testing
        db.ContractStates.Add(new ContractStateLookup { LookupSetId = (int)OnlineContract.Helpers.ProductStateInOrder.Draft, IsStartState = true, IsEndState = false });
        db.ContractStates.Add(new ContractStateLookup { LookupSetId = (int)OnlineContract.Helpers.ProductStateInOrder.Submitted, IsStartState = false, IsEndState = false });
        db.ContractStates.Add(new ContractStateLookup { LookupSetId = (int)OnlineContract.Helpers.ProductStateInOrder.Accepted, IsStartState = false, IsEndState = false });
        db.ContractStates.Add(new ContractStateLookup { LookupSetId = (int)OnlineContract.Helpers.ProductStateInOrder.Rejected, IsStartState = false, IsEndState = true });

        // Seed one draft contract for user 123
        var contract = new Contract
        {
            Id = 500,
            InputUserId = 123,
            ContractState = OnlineContract.Helpers.ContractState.Submitted,
            EntryDate = DateTime.UtcNow,
            Amount = 0m,
            IsActive = true,
            IsDeleted = false,
            Stamp = 0
        };
        db.Contracts.Add(contract);

        // Seed two items for the contract
        var det1 = new ContractDet
        {
            Id = 501,
            ContractId = 500,
            ProductVariantId = 101,
            Quantity = 2,
            Amount = 1500m,
            ProductName = "T-Shirt",
            Size = "M",
            Color = "Red",
            ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft,
            InputDt = DateTime.UtcNow,
            InputUserId = 123,
            IsActive = true,
            IsDeleted = false,
            Stamp = 0
        };
        var det2 = new ContractDet
        {
            Id = 502,
            ContractId = 500,
            ProductVariantId = 102,
            Quantity = 1,
            Amount = 1700m,
            ProductName = "T-Shirt",
            Size = "L",
            Color = "Blue",
            ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Rejected,
            InputDt = DateTime.UtcNow,
            InputUserId = 123,
            IsActive = true,
            IsDeleted = false,
            Stamp = 0
        };
        db.ContractDets.AddRange(det1, det2);

        // Update contract amount to include items
        contract.Amount = det1.Quantity * det1.Amount + det2.Quantity * det2.Amount;

        db.SaveChanges();
    }
}
