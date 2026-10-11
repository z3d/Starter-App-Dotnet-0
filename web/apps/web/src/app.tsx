import { claims, signOut } from "./lib/auth";
import { Walkthrough } from "./sample/Walkthrough";

export function App() {
  const who = claims();
  return (
    <div className="mx-auto max-w-3xl px-4 py-8">
      <header role="banner" className="mb-8 flex items-baseline justify-between gap-4 border-b border-neutral-200 pb-4 dark:border-neutral-800">
        <h1 className="text-2xl font-semibold">StarterApp</h1>
        <div className="flex items-center gap-3 text-sm text-neutral-600 dark:text-neutral-400">
          <span>{String(who?.sub ?? "")}</span>
          <button type="button" onClick={signOut} className="rounded border border-neutral-300 px-2 py-1 hover:bg-neutral-100 dark:border-neutral-700 dark:hover:bg-neutral-800">Sign out</button>
        </div>
      </header>
      <main>
        <Walkthrough />
      </main>
    </div>
  );
}
