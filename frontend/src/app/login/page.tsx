
"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { login } from "@/lib/api";

export default function LoginPage() {
    const router = useRouter();

    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [error, setError] = useState("");
    const [isSubmitting, setIsSubmitting] = useState(false);

    async function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        setError("");
        setIsSubmitting(true);

        try {
            await login(email.trim(), password);
            router.replace("/");
        } catch (err) {
            setError(
                err instanceof Error
                    ? err.message
                    : "Unable to sign in. Please try again.",
            );
        } finally {
            setIsSubmitting(false);
        }
    }

    return (
        <main className="flex min-h-screen items-center justify-center bg-[#f7f8fa] px-4 py-10 text-slate-900">
            <div className="w-full max-w-md">
                <Link href="/" className="mb-8 flex items-center justify-center gap-3">
                    <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-slate-900 text-xl font-bold text-white">
                        L
                    </div>
                    <span className="text-2xl font-semibold tracking-tight">
                        Lexis
                    </span>
                </Link>

                <section className="rounded-2xl border border-slate-200 bg-white p-8 shadow-sm sm:p-10">
                    <div className="mb-8">
                        <h1 className="text-2xl font-semibold tracking-tight">
                            Welcome back
                        </h1>
                        <p className="mt-2 text-sm leading-6 text-slate-500">
                            Sign in to access your documents and ask questions with Lexis.
                        </p>
                    </div>

                    <form onSubmit={handleSubmit} className="space-y-5">
                        <div>
                            <label
                                htmlFor="email"
                                className="mb-2 block text-sm font-medium text-slate-700"
                            >
                                Email address
                            </label>
                            <input
                                id="email"
                                type="email"
                                autoComplete="email"
                                required
                                value={email}
                                onChange={(event) => setEmail(event.target.value)}
                                placeholder="you@example.com"
                                className="w-full rounded-xl border border-slate-300 px-4 py-3 text-sm outline-none transition focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                            />
                        </div>

                        <div>
                            <label
                                htmlFor="password"
                                className="mb-2 block text-sm font-medium text-slate-700"
                            >
                                Password
                            </label>
                            <input
                                id="password"
                                type="password"
                                autoComplete="current-password"
                                required
                                value={password}
                                onChange={(event) => setPassword(event.target.value)}
                                placeholder="Enter your password"
                                className="w-full rounded-xl border border-slate-300 px-4 py-3 text-sm outline-none transition focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                            />
                        </div>

                        {error && (
                            <div
                                role="alert"
                                className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700"
                            >
                                {error}
                            </div>
                        )}

                        <button
                            type="submit"
                            disabled={isSubmitting}
                            className="w-full rounded-xl bg-slate-900 px-4 py-3 text-sm font-medium text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            {isSubmitting ? "Signing in..." : "Sign in"}
                        </button>
                    </form>

                    <p className="mt-6 text-center text-sm text-slate-500">
                        Need an account?{" "}
                        <Link href="/register" className="font-medium text-slate-900 underline underline-offset-4">
                            Create one
                        </Link>
                    </p>
                </section>

                <p className="mt-6 text-center text-xs text-slate-400">
                    Your documents remain associated with your account.
                </p>
            </div>
        </main>
    );
}
