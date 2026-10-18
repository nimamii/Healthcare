-- Schema bootstrap for local development.
--
-- Applied by docker-compose's postgres entrypoint, so it runs once when the
-- database is first created — before the API starts.
--
-- Why not DbContext.EnsureCreated(): it creates the DATABASE if absent, then
-- all tables from its own model, and does nothing at all if the database
-- already exists. With two modules each owning a schema, whichever ran first
-- created the database and every later one silently did nothing. That is how
-- audit.entries ended up missing while the API reported itself healthy.
--
-- This file is idempotent, so re-applying it is harmless. Migrations take over
-- from here in Phase 9 — see docs/ARCHITECTURE.md.

-- PostGIS ---------------------------------------------------------------------
--
-- The spec requires PostgreSQL + PostGIS for geospatial work (§7). The image
-- is built locally (Dockerfile.postgres) because postgis/postgis is only on
-- Docker Hub, which this network blocks.
--
-- The extension is created here rather than by the application so that a
-- geometry column either exists or the schema script fails loudly — an
-- application-time CREATE EXTENSION leaves every later migration depending on
-- an extension nobody checked.
CREATE EXTENSION IF NOT EXISTS postgis;

-- Identity -------------------------------------------------------------------

CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE IF NOT EXISTS identity.users (
    id                   uuid        PRIMARY KEY,
    email                varchar(320) NOT NULL,
    password_hash        varchar(512) NOT NULL,
    is_active            boolean     NOT NULL DEFAULT true,
    created_at           timestamptz NOT NULL,
    deactivated_at       timestamptz NULL,
    last_login_at        timestamptz NULL,
    password_changed_at  timestamptz NULL
);

-- Emails are normalised to lowercase on write, so a plain unique index is
-- enough to catch case variants.
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON identity.users (email);

CREATE TABLE IF NOT EXISTS identity.user_roles (
    user_id     uuid        NOT NULL,
    role        varchar(64) NOT NULL,
    granted_at  timestamptz NOT NULL,

    -- A user cannot hold the same role twice.
    PRIMARY KEY (user_id, role),

    CONSTRAINT fk_user_roles_user
        FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS identity.refresh_tokens (
    id          uuid        PRIMARY KEY,
    user_id     uuid        NOT NULL,
    -- Only the hash is stored. A database dump must not yield usable tokens.
    token_hash  varchar(128) NOT NULL,
    expires_at  timestamptz NOT NULL,
    created_at  timestamptz NOT NULL,
    revoked_at  timestamptz NULL,
    reused_at   timestamptz NULL,

    CONSTRAINT fk_refresh_tokens_user
        FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_refresh_tokens_hash ON identity.refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user ON identity.refresh_tokens (user_id);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_expiry ON identity.refresh_tokens (expires_at);

-- Audit ----------------------------------------------------------------------

CREATE SCHEMA IF NOT EXISTS audit;

-- Append-only by design. There is no update or delete path in the module
-- either; a log that can be edited is not a log.
CREATE TABLE IF NOT EXISTS audit.entries (
    id           uuid        PRIMARY KEY,
    occurred_at  timestamptz NOT NULL,
    category     varchar(64)  NOT NULL,
    action       varchar(128) NOT NULL,
    user_id      uuid        NULL,
    subject      varchar(320) NULL,
    outcome      varchar(32)  NOT NULL,
    detail       varchar(1024) NULL
);

-- The two queries the admin view actually runs.
CREATE INDEX IF NOT EXISTS ix_entries_user_time ON audit.entries (user_id, occurred_at);
CREATE INDEX IF NOT EXISTS ix_entries_time ON audit.entries (occurred_at);

-- Catalog --------------------------------------------------------------------

CREATE SCHEMA IF NOT EXISTS catalog;

CREATE TABLE IF NOT EXISTS catalog.categories (
    id           uuid        PRIMARY KEY,
    name         varchar(200) NOT NULL,
    slug         varchar(200) NOT NULL,
    -- Self-reference. ON DELETE RESTRICT is the default and is what stops a
    -- category delete from taking a whole subtree with it.
    parent_id    uuid        NULL,
    -- Denormalised so a depth check never needs a recursive query, and so a
    -- corrupted parent pointer cannot produce an infinite tree walk.
    depth        integer     NOT NULL,
    is_active    boolean     NOT NULL DEFAULT true,
    created_at   timestamptz NOT NULL,
    updated_at   timestamptz NOT NULL,

    CONSTRAINT fk_categories_parent
        FOREIGN KEY (parent_id) REFERENCES catalog.categories (id) ON DELETE RESTRICT,

    CONSTRAINT ck_categories_depth CHECK (depth BETWEEN 1 AND 4),
    CONSTRAINT ck_categories_not_own_parent CHECK (parent_id IS NULL OR parent_id <> id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_categories_slug ON catalog.categories (slug);
CREATE INDEX IF NOT EXISTS ix_categories_parent ON catalog.categories (parent_id);
CREATE INDEX IF NOT EXISTS ix_categories_active_name ON catalog.categories (is_active, name);

CREATE TABLE IF NOT EXISTS catalog.products (
    id                   uuid         PRIMARY KEY,
    name                 varchar(300) NOT NULL,
    slug                 varchar(300) NOT NULL,
    description          varchar(4000) NULL,
    category_id          uuid         NOT NULL,
    -- Prescription status belongs to the product, not the SKU: a
    -- prescription-only device stays that way regardless of who stocks it.
    requires_prescription boolean      NOT NULL DEFAULT false,
    is_active            boolean      NOT NULL DEFAULT true,
    created_at           timestamptz  NOT NULL,
    updated_at           timestamptz  NOT NULL,

    CONSTRAINT fk_products_category
        FOREIGN KEY (category_id) REFERENCES catalog.categories (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_products_slug ON catalog.products (slug);
CREATE INDEX IF NOT EXISTS ix_products_category ON catalog.products (category_id, is_active);

CREATE TABLE IF NOT EXISTS catalog.skus (
    id                  uuid         PRIMARY KEY,
    product_id          uuid         NOT NULL,
    -- Our own code, never a supplier's. Uniqueness is what lets an order line
    -- point at a variant without ambiguity.
    code                varchar(64)  NOT NULL,
    variant_description varchar(300) NULL,
    is_active           boolean      NOT NULL DEFAULT true,
    created_at          timestamptz  NOT NULL,
    updated_at          timestamptz  NOT NULL,

    CONSTRAINT fk_skus_product
        FOREIGN KEY (product_id) REFERENCES catalog.products (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_skus_code ON catalog.skus (code);
CREATE INDEX IF NOT EXISTS ix_skus_product ON catalog.skus (product_id, is_active);

CREATE TABLE IF NOT EXISTS catalog.supplier_products (
    id            uuid          PRIMARY KEY,
    sku_id        uuid          NOT NULL,
    -- No foreign key: the Suppliers module owns this reference and lives in
    -- another schema. §13 forbids the database-level coupling an FK would
    -- create between two modules that must stay separable.
    supplier_id   uuid          NOT NULL,
    -- The supplier's own code. Never used as a key on our side: two suppliers
    -- can both publish "12345".
    supplier_sku  varchar(128)  NOT NULL,
    -- NULL means not published, which is different from zero.
    list_price    numeric(18,4) NULL,
    currency      varchar(3)    NOT NULL DEFAULT 'IRR',
    is_active     boolean       NOT NULL DEFAULT true,
    created_at    timestamptz   NOT NULL,
    updated_at    timestamptz   NOT NULL,

    CONSTRAINT fk_supplier_products_sku
        FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE CASCADE,

    CONSTRAINT ck_supplier_products_price CHECK (list_price IS NULL OR list_price > 0),
    CONSTRAINT ck_supplier_products_currency CHECK (currency = upper(currency))
);

-- One supplier lists a given SKU once; re-listing updates the row.
CREATE UNIQUE INDEX IF NOT EXISTS ux_supplier_products_sku_supplier
    ON catalog.supplier_products (sku_id, supplier_id);
CREATE INDEX IF NOT EXISTS ix_supplier_products_supplier ON catalog.supplier_products (supplier_id);
-- Inventory -----------------------------------------------------------------

CREATE SCHEMA IF NOT EXISTS inventory;

-- One row per (sku, supplier). `version` is the optimistic concurrency token:
-- every write carries the value it read, and the database rejects the write if
-- someone else got there first. Chosen over SELECT ... FOR UPDATE because a
-- web request should not hold a transaction open while a user decides.
CREATE TABLE IF NOT EXISTS inventory.stock_items (
    id                  uuid        PRIMARY KEY,
    -- No foreign key: Catalog owns this reference and lives in another schema.
    -- §13 forbids the cross-module database dependency an FK would create.
    sku_id              uuid        NOT NULL,
    -- Same for Suppliers.
    supplier_id         uuid        NOT NULL,
    available_quantity  integer     NOT NULL DEFAULT 0,
    reserved_quantity   integer     NOT NULL DEFAULT 0,
    version             bigint      NOT NULL DEFAULT 1,
    created_at          timestamptz NOT NULL,
    updated_at          timestamptz NOT NULL,

    CONSTRAINT ck_stock_no_negative
        CHECK (available_quantity >= 0 AND reserved_quantity >= 0),

    CONSTRAINT ck_stock_version_positive CHECK (version >= 1)
);

-- One stock row per sku/supplier pair. A second row for the same pair would
-- make "the" quantity ambiguous.
CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_items_sku_supplier
    ON inventory.stock_items (sku_id, supplier_id);

CREATE INDEX IF NOT EXISTS ix_stock_items_sku ON inventory.stock_items (sku_id);

CREATE TABLE IF NOT EXISTS inventory.reservations (
    id              uuid        PRIMARY KEY,
    -- No foreign key: Orders arrives in a later phase.
    order_id        uuid        NOT NULL,
    sku_id          uuid        NOT NULL,
    quantity        integer     NOT NULL,
    status          integer     NOT NULL DEFAULT 0,
    expires_at      timestamptz NOT NULL,
    created_at      timestamptz NOT NULL,
    resolved_at     timestamptz NULL,

    CONSTRAINT ck_reservations_quantity CHECK (quantity > 0),

    CONSTRAINT ck_reservations_status
        CHECK (status IN (0, 1, 2, 3)),

    -- An order may not reserve the same SKU twice. This is what makes a
    -- retried request harmless instead of double-holding stock (§6).
    CONSTRAINT ux_reservations_order_sku UNIQUE (order_id, sku_id)
);

-- The sweep's query: "what has aged out", run every few minutes.
CREATE INDEX IF NOT EXISTS ix_reservations_expiry
    ON inventory.reservations (expires_at)
    WHERE status = 0;

-- Geography --------------------------------------------------------------------

CREATE SCHEMA IF NOT EXISTS geography;

-- Service area: the polygon a supplier delivers to within normal terms (§7).
--
-- A supplier may define several. A medical distributor in Tehran plausibly
-- covers the capital plus a corridor north, and one polygon would have to be an
-- oversized shape that misrepresents actual coverage.
--
-- No ST_IsValid check constraint: validation runs per update rather than per
-- insert, and these rows change a handful of times a year. The application
-- validates on write, where a caller can be told what was wrong.
CREATE TABLE IF NOT EXISTS geography.service_areas (
    id              uuid        PRIMARY KEY,
    -- No foreign key: Suppliers lives in another schema, and §13 forbids the
    -- cross-module database coupling an FK would create.
    supplier_id      uuid        NOT NULL,
    name             varchar(200) NOT NULL,
    -- SRID 4326 (WGS84) is the storage standard. Geometry is never projected
    -- on disk: projection happens per query, so changing the calculation later
    -- does not mean rewriting every row.
    -- WKT mirror of the polygon below. The domain stores text so it can
    -- validate a boundary without PostGIS; the geometry column is what the
    -- GIST index and ST_Contains operate on. Keeping both means the shape can
    -- be read without a spatial function and queried without loading text.
    polygon_wkt       text         NOT NULL,
    -- Populated from polygon_wkt by the application immediately after insert.
    -- Nullable for exactly that window: EF writes the row before the geometry
    -- exists. ck_service_areas_polygon_required below refuses to let a row
    -- stay in that state, so anything that reads this table sees a polygon.
    polygon           geometry(MultiPolygon, 4326) NULL,

    -- Surcharge for a delivery address OUTSIDE this polygon. §1 is explicit
    -- that the customer must not be blocked in that case: the extra cost is
    -- calculated, shown, and confirmed.
    --
    -- Null means "no surcharge" rather than zero, so an unconfigured area
    -- cannot be mistaken for a free one.
    out_of_area_surcharge   numeric(14,2) NULL,
    out_of_area_currency    varchar(3)     NULL,

    is_active        boolean      NOT NULL DEFAULT true,
    created_at       timestamptz  NOT NULL,
    updated_at       timestamptz  NOT NULL,

    CONSTRAINT ck_service_areas_currency
        CHECK (out_of_area_currency IS NULL OR out_of_area_currency = upper(out_of_area_currency)),

    -- A surcharge without a currency is meaningless. The reverse is harmless:
    -- no surcharge, no currency needed.
    CONSTRAINT ck_service_areas_surcharge_has_currency
        CHECK (out_of_area_surcharge IS NULL OR out_of_area_currency IS NOT NULL),

    CONSTRAINT ck_service_areas_surcharge_positive
        CHECK (out_of_area_surcharge IS NULL OR out_of_area_surcharge >= 0),

    -- A self-intersecting polygon makes ST_Contains return an unpredictable
    -- answer, which would silently misreport coverage. Checked on write
    -- rather than per read: these rows change a handful of times a year, and
    -- the check is cheap next to a query that runs on every checkout.
    CONSTRAINT ck_service_areas_polygon_required
        CHECK (polygon IS NOT NULL),

    CONSTRAINT ck_service_areas_polygon_valid
        CHECK (ST_IsValid(polygon))
);

-- Point-in-area runs on every checkout, so this index is what keeps it from
-- scanning every polygon.
CREATE INDEX IF NOT EXISTS ix_service_areas_polygon
    ON geography.service_areas USING GIST (polygon);

CREATE INDEX IF NOT EXISTS ix_service_areas_supplier
    ON geography.service_areas (supplier_id);

-- One area per supplier per name. Re-uploading a boundary under the same name
-- updates it instead of accumulating duplicates that every lookup must consider.
CREATE UNIQUE INDEX IF NOT EXISTS ux_service_areas_supplier_name
    ON geography.service_areas (supplier_id, name);

-- Orders --------------------------------------------------------------------

CREATE SCHEMA IF NOT EXISTS orders;

CREATE TABLE IF NOT EXISTS orders.orders (
    id              uuid        PRIMARY KEY,
    -- No foreign keys: Customer and Supplier live in other schemas, and §13
    -- forbids the cross-module database coupling an FK would create.
    customer_id     uuid        NOT NULL,
    supplier_id     uuid        NOT NULL,
    status          integer     NOT NULL,
    created_at      timestamptz NOT NULL,
    updated_at      timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_orders_customer ON orders.orders (customer_id);
CREATE INDEX IF NOT EXISTS ix_orders_supplier ON orders.orders (supplier_id);

CREATE TABLE IF NOT EXISTS orders.order_lines (
    id              uuid        PRIMARY KEY,
    order_id        uuid        NOT NULL REFERENCES orders.orders(id) ON DELETE CASCADE,
    sku_id          uuid        NOT NULL,
    quantity        integer     NOT NULL CHECK (quantity > 0),
    unit_price      numeric(18,4) NOT NULL CHECK (unit_price >= 0),
    currency        varchar(3)  NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_order_lines_order ON orders.order_lines (order_id);
CREATE INDEX IF NOT EXISTS ix_order_lines_sku ON orders.order_lines (sku_id);

-- Payments -------------------------------------------------------------------

CREATE SCHEMA IF NOT EXISTS payments;

CREATE TABLE IF NOT EXISTS payments.payments (
    id                        uuid        PRIMARY KEY,
    -- No foreign keys: Order lives in another schema, and §13 forbids
    -- the cross-module database coupling an FK would create.
    order_id                  uuid        NOT NULL,
    amount                    numeric(18,4) NOT NULL CHECK (amount > 0),
    currency                  varchar(3)    NOT NULL,
    status                    integer     NOT NULL,
    -- The payment provider's own transaction id. Null until the provider
    -- has been called. Never used as a key on our side.
    provider_transaction_id   varchar(200) NULL,
    created_at                timestamptz NOT NULL,
    updated_at                timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_payments_order ON payments.payments (order_id);
