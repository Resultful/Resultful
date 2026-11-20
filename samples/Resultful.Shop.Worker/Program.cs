// The Shop's queue worker. Use case 6 runs only when it is compiled (Shop.targets); otherwise this does nothing.
#if SHOP_UC06
await Resultful.Shop.Worker.QueueWorker.RunAsync(Console.Out);
#else
await Task.CompletedTask;
#endif
