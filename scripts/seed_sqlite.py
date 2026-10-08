"""
SQLite High-Speed Seeding Script for TwitterBenchmark
Generates 50,000 users, 500,000 posts, and 2,000,000 likes with WAL mode enabled.
"""
import sqlite3
import time
import os

DB_FILE = os.path.join(os.path.dirname(__file__), "..", "twitter_bench.db")

def seed():
    print(f"[*] Seeding SQLite database at: {DB_FILE}")
    start = time.time()

    if os.path.exists(DB_FILE):
        os.remove(DB_FILE)

    conn = sqlite3.connect(DB_FILE)
    cur = conn.cursor()

    # Enable WAL mode and speed optimizations
    cur.execute("PRAGMA journal_mode = WAL;")
    cur.execute("PRAGMA synchronous = OFF;")
    cur.execute("PRAGMA cache_size = -128000;") # 128MB cache during seeding
    cur.execute("PRAGMA temp_store = MEMORY;")

    # Schema
    cur.execute("""
        CREATE TABLE users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL DEFAULT (datetime('now'))
        );
    """)

    cur.execute("""
        CREATE TABLE posts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL REFERENCES users(id),
            content TEXT NOT NULL,
            created_at TEXT NOT NULL DEFAULT (datetime('now'))
        );
    """)

    cur.execute("""
        CREATE TABLE likes (
            user_id INTEGER NOT NULL REFERENCES users(id),
            post_id INTEGER NOT NULL REFERENCES posts(id),
            created_at TEXT NOT NULL DEFAULT (datetime('now')),
            PRIMARY KEY (user_id, post_id)
        );
    """)

    conn.commit()

    # 1. Seed 50,000 users
    print("[1/3] Inserting 50,000 users...")
    batch_users = [(f"user_{i}", f"2026-09-01 12:00:{i%60:02d}") for i in range(1, 50001)]
    cur.executemany("INSERT INTO users (username, created_at) VALUES (?, ?);", batch_users)
    conn.commit()

    # 2. Seed 500,000 posts
    print("[2/3] Inserting 500,000 posts...")
    post_chunk = 50000
    for chunk_start in range(1, 500001, post_chunk):
        posts = [
            (
                ((i % 50000) + 1),
                f"Post #{i}: Real-world benchmark post simulating Twitter/X feed activity.",
                f"2026-09-15 10:{((i//60)%60):02d}:{(i%60):02d}"
            )
            for i in range(chunk_start, chunk_start + post_chunk)
        ]
        cur.executemany("INSERT INTO posts (user_id, content, created_at) VALUES (?, ?, ?);", posts)
        conn.commit()

    # 3. Seed 2,000,000 likes
    print("[3/3] Inserting 2,000,000 likes...")
    like_chunk = 100000
    for chunk_start in range(1, 2000001, like_chunk):
        likes = [
            (
                ((i % 50000) + 1),
                ((i * 17) % 500000 + 1),
                f"2026-09-20 14:{((i//60)%60):02d}:{(i%60):02d}"
            )
            for i in range(chunk_start, chunk_start + like_chunk)
        ]
        cur.executemany("INSERT OR IGNORE INTO likes (user_id, post_id, created_at) VALUES (?, ?, ?);", likes)
        conn.commit()

    # 4. Indexes
    print("[*] Creating indexes...")
    cur.execute("CREATE INDEX idx_posts_created_at_desc ON posts (created_at DESC);")
    cur.execute("CREATE INDEX idx_posts_user_id ON posts (user_id);")
    cur.execute("CREATE INDEX idx_likes_post_id ON likes (post_id);")
    conn.commit()

    # Reset pragma to normal
    cur.execute("PRAGMA synchronous = NORMAL;")
    conn.close()

    elapsed = time.time() - start
    size_mb = os.path.getsize(DB_FILE) / (1024 * 1024)
    print(f"[✓] Seeding completed in {elapsed:.2f}s! Database size: {size_mb:.1f} MB")

if __name__ == "__main__":
    seed()
