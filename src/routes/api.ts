import { Router, Response } from 'express';
import { AppRequest } from '../models/RequestMetadata';

/** Application routes. Kept tiny — the interesting part is the middleware. */
export const apiRouter = Router();

apiRouter.get('/hello', (req: AppRequest, res: Response) => {
  res.json({
    message: 'Hello World',
    apiKey: req.apiKey,
    requestId: req.requestId,
  });
});
