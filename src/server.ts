import { createApp } from './app';
import { config } from './config';

const app = createApp();

app.listen(config.port, () => {
  // eslint-disable-next-line no-console
  console.log(
    `RateGuard listening on http://localhost:${config.port} ` +
      `(capacity=${config.capacity}, refillRate=${config.refillRate}/s)`,
  );
});
