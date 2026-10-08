import http from 'k6/http';
import { check, sleep } from 'k6';

// Read configuration from environment
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const TARGET_USERS = parseInt(__ENV.USERS || '1000', 10);
const DURATION = __ENV.DURATION || '2m';

export const options = {
  stages: [
    { duration: '30s', target: TARGET_USERS }, // ramp up
    { duration: DURATION, target: TARGET_USERS }, // hold peak
    { duration: '15s', target: 0 },             // ramp down
  ],
  thresholds: {
    // Arjay's exact passing criteria:
    // P95 latency <= 500ms
    // P99 latency <= 1000ms
    // Error rate <= 1%
    'http_req_duration': ['p(95)<500', 'p(99)<1000'],
    'http_req_failed': ['rate<0.01'],
  },
};

export default function () {
  const userId = (__VU % 50000) + 1;
  const authHeaders = {
    'Authorization': `Bearer user_${userId}`,
    'Content-Type': 'application/json',
  };

  // 1. Load Feed
  const feedRes = http.get(`${BASE_URL}/feed`, { headers: authHeaders });
  check(feedRes, {
    'feed status is 200': (r) => r.status === 200,
  });

  // Simulated think time: 3 to 7 seconds
  sleep(Math.random() * 4 + 3);

  // 2. Open a Post (Pick random post ID or from feed)
  let postId = Math.floor(Math.random() * 500000) + 1;
  if (feedRes.status === 200) {
    try {
      const feed = JSON.parse(feedRes.body);
      if (Array.isArray(feed) && feed.length > 0) {
        const randomItem = feed[Math.floor(Math.random() * feed.length)];
        if (randomItem && randomItem.id) {
          postId = randomItem.id;
        }
      }
    } catch (e) {
      // fallback to random postId
    }
  }

  const postRes = http.get(`${BASE_URL}/posts/${postId}`, { headers: authHeaders });
  check(postRes, {
    'post status is 200': (r) => r.status === 200,
  });

  // Simulated think time: 3 to 7 seconds
  sleep(Math.random() * 4 + 3);

  // 3. Sometimes like a post (approx 20% chance)
  if (Math.random() < 0.20) {
    const likeRes = http.post(`${BASE_URL}/posts/${postId}/like`, null, { headers: authHeaders });
    check(likeRes, {
      'like status is 200': (r) => r.status === 200,
    });
    sleep(Math.random() * 2 + 1);
  }

  // 4. Sometimes create a post (approx 5% chance)
  if (Math.random() < 0.05) {
    const payload = JSON.stringify({
      content: `Hello from VU ${__VU} at ${new Date().toISOString()}!`,
    });
    const createRes = http.post(`${BASE_URL}/posts`, payload, { headers: authHeaders });
    check(createRes, {
      'create post status is 201': (r) => r.status === 201,
    });
    sleep(Math.random() * 2 + 1);
  }
}
