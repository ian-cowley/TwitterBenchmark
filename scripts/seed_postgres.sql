-- PostgreSQL Seeding Script for TwitterBenchmark
-- Seeds 50,000 users, 500,000 posts, and 2,000,000 likes in ~10 seconds

DROP TABLE IF EXISTS likes;
DROP TABLE IF EXISTS posts;
DROP TABLE IF EXISTS users;

CREATE TABLE users (
    id BIGSERIAL PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE posts (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(id),
    content TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE likes (
    user_id BIGINT NOT NULL REFERENCES users(id),
    post_id BIGINT NOT NULL REFERENCES posts(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, post_id)
);

-- 1. Insert 50,000 users
INSERT INTO users (id, username, created_at)
SELECT 
    i, 
    'user_' || i, 
    NOW() - (i || ' seconds')::INTERVAL
FROM generate_series(1, 50000) AS i;

-- 2. Insert 500,000 posts (distributed across 50,000 users)
INSERT INTO posts (id, user_id, content, created_at)
SELECT 
    i, 
    ((i % 50000) + 1), 
    'Post #' || i || ': Real-world benchmark post simulating Twitter/X feed activity.', 
    NOW() - (i || ' seconds')::INTERVAL
FROM generate_series(1, 500000) AS i;

-- 3. Insert 2,000,000 likes
-- Skewed distribution: newer posts get more likes
INSERT INTO likes (user_id, post_id, created_at)
SELECT 
    ((i % 50000) + 1) AS user_id,
    ((i * 17) % 500000 + 1) AS post_id,
    NOW() - ((i % 100000) || ' seconds')::INTERVAL
FROM generate_series(1, 2000000) AS i
ON CONFLICT (user_id, post_id) DO NOTHING;

-- 4. Create Indexes
CREATE INDEX idx_posts_created_at_desc ON posts (created_at DESC);
CREATE INDEX idx_posts_user_id ON posts (user_id);
CREATE INDEX idx_likes_post_id ON likes (post_id);

-- Reset sequences
SELECT setval('users_id_seq', (SELECT MAX(id) FROM users));
SELECT setval('posts_id_seq', (SELECT MAX(id) FROM posts));

-- Optimize and update statistics
VACUUM ANALYZE;
