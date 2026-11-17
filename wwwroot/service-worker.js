// In development, always fetch from the network and do not enable offline support.
// The published version (service-worker.published.js) caches the app for offline use at the gym.
self.addEventListener('fetch', () => { });
