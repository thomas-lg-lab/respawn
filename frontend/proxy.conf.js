// The dev server has no API of its own: it hands /api and /healthz to the app, started
// beside it (docker compose up --build). RESPAWN_API says where, when that is not this
// machine's port 8080.
module.exports = {
  '/api': { target: process.env.RESPAWN_API ?? 'http://localhost:8080' },
  '/healthz': { target: process.env.RESPAWN_API ?? 'http://localhost:8080' },
};
