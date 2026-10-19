# PostgreSQL 17 with pgvector and PostGIS, the extensions the catalog uses on
# Azure Database for PostgreSQL Flexible Server. The pgvector image is the official
# postgres image (Debian trixie) with pgvector added; PostGIS comes from the same PGDG repository.
FROM pgvector/pgvector:0.8.7-pg17-trixie

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-17-postgis-3 postgresql-17-postgis-3-scripts \
    && rm -rf /var/lib/apt/lists/*
