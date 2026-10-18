// Was: InternalsVisibleTo for InventoryDbContext to call StockItem.Touch().
//
// No longer needed: the version token is advanced in SaveChangesAsync, which
// is inside the same assembly as the context. Delete this file.
//