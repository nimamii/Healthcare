// ServiceEligibility moved to Geography.Contracts.
//
// It was defined here first, which created two identical types — one here and
// one in Contracts — and every call site needed a mapper between them. That is
// the opposite of what Contracts is for: it exists so a single type crosses
// the module boundary without the domain assembly leaking.
//
// Every other module in this repository already does it this way: UserSummary,
// ProductSummary and StockLevel each live only in Contracts.
//
// Delete this file.
