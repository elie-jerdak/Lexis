
"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { register } from "@/lib/api";

export default function RegisterPage() {
    const [firstName, setFirstName] = useState("");
    const [lastName, setLastName] = useState("");
    const [email, setEmail] = useState("");
    const [phoneNumber, setPhoneNumber] = useState("");
    const [password, setPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");
    const [error, setError] = useState("");
    const [success, setSuccess] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);

    async function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        setError("");

        if (password.length < 8 || password.length > 13) {
            setError("Password must be between 8 and 13 characters.");
            return;
        }

        if (password !== confirmPassword) {
            setError("Passwords do not match.");
            return;
        }

        setIsSubmitting(true);

        try {
            await register({
                firstName: firstName.trim(),
                lastName: lastName.trim(),
                email: email.trim(),
                phoneNumber: phoneNumber.trim(),
                password,
            });

            setSuccess(true);
        } catch (err) {
            setError(
                err instanceof Error
                    ? err.message
                    : "Unable to create your account. Please try again.",
            );
        } finally {
            setIsSubmitting(false);
        }
    }

    return (
        <main className="flex min-h-screen items-center justify-center bg-[#f7f8fa] px-4 py-10 text-slate-900">
            <div className="w-full max-w-md">
                <Link
                    href="/login"
                    className="mb-8 flex items-center justify-center gap-3"
                >
                    <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-slate-900 text-xl font-bold text-white">
                        L
                    </div>
                    <span className="text-2xl font-semibold tracking-tight">
                        Lexis
                    </span>
                </Link>

                <section className="rounded-2xl border border-slate-200 bg-white p-8 shadow-sm sm:p-10">
                    {success ? (
                        <>
                            <h1 className="text-2xl font-semibold tracking-tight">
                                Check your email
                            </h1>
                            <p className="mt-3 text-sm leading-6 text-slate-600">
                                Your account was created successfully. Follow
                                the confirmation link sent to your email
                                address before signing in.
                            </p>
                            <Link
                                href="/login"
                                className="mt-6 flex w-full justify-center rounded-xl bg-slate-900 px-4 py-3 text-sm font-medium text-white transition hover:bg-slate-700"
                            >
                                Go to sign in
                            </Link>
                        </>
                    ) : (
                        <>
                            <div className="mb-8">
                                <h1 className="text-2xl font-semibold tracking-tight">
                                    Create your account
                                </h1>
                                <p className="mt-2 text-sm leading-6 text-slate-500">
                                    Create a Lexis account to manage your
                                    documents and ask questions.
                                </p>
                            </div>

                            <form onSubmit={handleSubmit} className="space-y-4">
                                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                                    <div>
                                        <label htmlFor="firstName" className="mb-2 block text-sm font-medium text-slate-700">
                                            First name
                                        </label>
                                        <input
                                            id="firstName"
                                            required
                                            maxLength={50}
                                            autoComplete="given-name"
                                            value={firstName}
                                            onChange={(e) => setFirstName(e.target.value)}
                                            className="w-full rounded-xl border border-slate-300 px-3 py-3 text-sm outline-none focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                                        />
                                    </div>

                                    <div>
                                        <label htmlFor="lastName" className="mb-2 block text-sm font-medium text-slate-700">
                                            Last name
                                        </label>
                                        <input
                                            id="lastName"
                                            required
                                            maxLength={50}
                                            autoComplete="family-name"
                                            value={lastName}
                                            onChange={(e) => setLastName(e.target.value)}
                                            className="w-full rounded-xl border border-slate-300 px-3 py-3 text-sm outline-none focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <label htmlFor="email" className="mb-2 block text-sm font-medium text-slate-700">
                                        Email address
                                    </label>
                                    <input
                                        id="email"
                                        type="email"
                                        required
                                        maxLength={100}
                                        autoComplete="email"
                                        value={email}
                                        onChange={(e) => setEmail(e.target.value)}
                                        placeholder="you@example.com"
                                        className="w-full rounded-xl border border-slate-300 px-4 py-3 text-sm outline-none focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                                    />
                                </div>

                                <div>
                                    <label htmlFor="phoneNumber" className="mb-2 block text-sm font-medium text-slate-700">
                                        Phone number <span className="font-normal text-slate-400">(optional)</span>
                                    </label>
                                    <input
                                        id="phoneNumber"
                                        type="tel"
                                        autoComplete="tel"
                                        value={phoneNumber}
                                        onChange={(e) => setPhoneNumber(e.target.value)}
                                        className="w-full rounded-xl border border-slate-300 px-4 py-3 text-sm outline-none focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                                    />
                                </div>

                                <div>
                                    <label htmlFor="password" className="mb-2 block text-sm font-medium text-slate-700">
                                        Password
                                    </label>
                                    <input
                                        id="password"
                                        type="password"
                                        required
                                        minLength={8}
                                        maxLength={13}
                                        autoComplete="new-password"
                                        value={password}
                                        onChange={(e) => setPassword(e.target.value)}
                                        className="w-full rounded-xl border border-slate-300 px-4 py-3 text-sm outline-none focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                                    />
                                    <p className="mt-1 text-xs text-slate-400">
                                        Use 8–13 characters.
                                    </p>
                                </div>

                                <div>
                                    <label htmlFor="confirmPassword" className="mb-2 block text-sm font-medium text-slate-700">
                                        Confirm password
                                    </label>
                                    <input
                                        id="confirmPassword"
                                        type="password"
                                        required
                                        autoComplete="new-password"
                                        value={confirmPassword}
                                        onChange={(e) => setConfirmPassword(e.target.value)}
                                        className="w-full rounded-xl border border-slate-300 px-4 py-3 text-sm outline-none focus:border-slate-900 focus:ring-2 focus:ring-slate-900/10"
                                    />
                                </div>

                                {error && (
                                    <div role="alert" className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                                        {error}
                                    </div>
                                )}

                                <button
                                    type="submit"
                                    disabled={isSubmitting}
                                    className="w-full rounded-xl bg-slate-900 px-4 py-3 text-sm font-medium text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-60"
                                >
                                    {isSubmitting ? "Creating account..." : "Create account"}
                                </button>
                            </form>

                            <p className="mt-6 text-center text-sm text-slate-500">
                                Already have an account?{" "}
                                <Link
                                    href="/login"
                                    className="font-medium text-slate-900 underline underline-offset-4"
                                >
                                    Sign in
                                </Link>
                            </p>
                        </>
                    )}
                </section>
            </div>
        </main>
    );
}
