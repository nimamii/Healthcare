FROM healthcare/postgres-postgis:16-3.4

# The entrypoint cannot load "postgis" as a shared library. Find out what the
# postgis packages actually installed and under which name.
RUN set -eux; \
    echo "=== postgis .so files ==="; \
    find / -name "postgis*.so" 2>/dev/null; \
    echo "=== any file named exactly postgis ==="; \
    find / -name "postgis" -type f 2>/dev/null | head; \
    echo "=== dpkg list ==="; \
    dpkg -L postgresql-16-postgis-3 2>/dev/null | grep -E "\.so|control|\.conf" | head -20; \
    echo "=== what does pg_config say? ==="; \
    ls /usr/lib/postgresql/16/lib/ | head -20
