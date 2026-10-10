namespace MovieRental.Host.Pages;

public static partial class InfoPages
{
    private static readonly InfoPage AboutEn = new("About WatchingYou",
        "A cinema, a film library and a home for short films made by its own audience — in one place.", [
        new("What we are", [
            "WatchingYou began as a neighbourhood cinema with a website for booking seats. It grew into three things that belong together: the cinema in Baku, a streaming library of classic and new films, and a community where members publish the short films they make themselves.",
            "Everything runs on one account. The same login books a seat for Friday night, rents a film for the weekend and uploads the short you shot on your phone.",
        ]),
        new("The cinema", [
            "Every screening has a live seat map: you see which seats are free as other people pick them, choose your own, pay, and get a ticket with a QR code. The code opens offline at the door, so a weak signal in the lobby never stops you.",
            "The schedule runs a week ahead with afternoon and evening shows. Cancelling is possible until 48 hours before a show; if we cancel a show ourselves, you get everything back.",
        ]),
        new("The library", [
            "Any film in the catalogue can be rented for three days for $0.50. There are no late fees: when the three days run out we simply ask whether you want three more.",
            "Watching PRO costs $5 a month and opens every film in the library for as long as the month lasts.",
        ]),
        new("Films by members", [
            "In Studio, any member can upload a short film and say how it was made: by hand, or with AI. Each upload is watched in full by our Security desk and approved by an administrator within three days before anyone else can see it.",
            "Approved films appear in the Handmade and AI galleries and on the author's profile. Private ones stay in the author's own drawer.",
        ]),
        new("People", [
            "Profiles work the way you know from Instagram: a picture, an @username, a bio, followers and a grid — here a grid of films. Accounts can be public or private; a private account approves every follower itself.",
            "Members who choose to appear on the globe can be found by city and written to directly. Anyone can block or report a message, and the Security desk reads every report.",
        ]),
        new("What we believe", [
            "No advertising, no selling of data, no tracking across other sites. Card numbers are never stored. A film with a small audience deserves the same care as a blockbuster, and a viewer deserves to know which films were made by a person and which by a machine.",
        ]),
    ]);

    private static readonly InfoPage BlogEn = new("Blog",
        "News from the cinema, the library and the studio.", [
        new("Profiles get a grid of films", [
            "Your profile now looks like the profiles you know: picture, @username, bio, follower counts, and below them a grid — of the films you made rather than of photos.",
            "Shared films are visible to everyone (or, for a private account, to the followers you accepted). A second tab, Private, is only for you. A switch on the right filters the grid by how a film was made: handmade or with AI.",
        ], Meta: "October 2026"),
        new("Private accounts and follow requests", [
            "A profile can now be private. Following it sends a request; the owner sees it with a red counter on their picture and answers with Accept or Decline. Once accepted, a Follow back button is right there.",
            "Switching an account back to public accepts every request still waiting, so nobody is left in limbo.",
        ], Meta: "October 2026"),
        new("How a short film gets published", [
            "Every upload passes two people. First the Security desk watches it in full and records a checklist — content, rights, audio, the declared origin. Then an administrator approves or rejects it with a note to the author.",
            "The whole review is promised within three days. Until then the film is visible to its author, who can follow the review and talk to the reviewers in Studio.",
        ], Meta: "September 2026"),
        new("Refunds, plainly", [
            "A ticket can be cancelled until 48 hours before the show; 30% of the price is kept to cover the seat we held for you. After that the seat is yours for good. If we cancel a show, you get the full amount back automatically.",
            "Each screening's page shows its own rules, because special events sometimes use different ones.",
        ], Meta: "September 2026"),
    ]);

    private static readonly InfoPage CareersEn = new("Careers",
        "A small team building a cinema, a library and a community at once.", [
        new("How we work", [
            "We are small, so everyone owns something end to end: the person who builds the seat map also stands in the lobby on opening night and watches people use it.",
            "We write things down, review each other's work, and prefer the simple solution that ships over the clever one that might.",
        ]),
        new("Who we look for", [
            "Engineers (.NET, React, SQL Server) who care about correctness: double bookings, payments and privacy are not places for shortcuts.",
            "Front-of-house and projection staff for the cinema, comfortable with the box office, the bar and the check-in scanner.",
            "Reviewers for the Security desk: people who can watch a film attentively and judge it fairly against written rules.",
            "Programmers and curators who know films and want to build the schedule and the library.",
        ]),
        new("What we offer", [
            "Free screenings and a Watching PRO account for you and one guest, flexible hours around the show schedule, and real responsibility from the first week.",
        ]),
        new("How to apply", [
            "Write to us with a few lines about yourself and something you made — code, a film, a programme of screenings. We answer every application.",
        ]),
    ]);

    private static readonly InfoPage DevelopersEn = new("API for developers",
        "Everything the website and the phone app do goes through the same HTTP API. Here is how to use it.", [
        new("Basics", [
            "The API speaks JSON over HTTPS. Times are UTC in ISO 8601. Money is a decimal number with its currency alongside. Lists come a page at a time.",
            "The website, the iOS and Android apps and the back office all use exactly the endpoints described here — there is no private, better API behind them.",
        ]),
        new("Public endpoints", ["These need no account:"], PublicEndpoints),
        new("Signing in", [
            "Sign in with e-mail and password to receive a short-lived access token (a JWT) and a refresh token. Send the access token as a Bearer header. When it expires, exchange the refresh token for a new pair; each refresh token works once, and using one twice signs the account out everywhere, because that only happens when a token was stolen.",
        ], AuthExample),
        new("Member endpoints", ["These need the Bearer token:"], MemberEndpoints),
        new("Errors", [
            "Errors use the standard Problem Details format. Validation errors list each field with its messages; 401 means sign in again, 403 means the account may not do this, 404 that the thing does not exist, 409 that it conflicts with something already there (a taken seat or username).",
        ], ErrorExample),
        new("Limits", [
            "Sign-in and registration allow 8 attempts per minute per address; verification codes 12 per 5 minutes. Exceeding a limit returns 429 Too Many Requests — wait and try again.",
            "Uploads: profile pictures up to 2 MB (JPEG, PNG, WebP); short films are checked by type and size before they are stored.",
        ]),
        new("Real time", [
            "Messages, typing indicators, seat changes and notifications arrive over a SignalR hub at /hubs/chat. Connect with the same access token (as the access_token query parameter, since WebSockets cannot carry headers).",
        ]),
        new("Full reference", [
            "The complete OpenAPI description, with every request and response type, is generated from the code and available to administrators at /swagger. If you are building something on top of WatchingYou, write to us and we will give you access.",
        ]),
    ]);

    private static readonly InfoPage TermsEn = new("Terms of use",
        "The rules for using WatchingYou. Short, because they should be read.", [
        new("Your account", [
            "You need an account to book, rent, upload or message. Give your real e-mail address — codes and tickets are sent there — and keep your password to yourself. You are responsible for what is done with your account.",
            "You can delete your account at any time on the Account page.",
        ]),
        new("Tickets", [
            "A ticket is valid for the screening, hall and seats printed on it, once. The QR code is checked at the door; a code that has been used cannot be used again.",
            "You can cancel until 48 hours before the show; 30% of the price is kept. Within 48 hours, or after the show has started, there is no refund. If we cancel a screening, the full price is returned. A screening's own page may set different rules, and those apply to it.",
        ]),
        new("Rentals and Watching PRO", [
            "A rental lets you watch one film for three days for $0.50; you can extend it by three more days for the same price. There are no late fees.",
            "Watching PRO costs $5 per month and gives access to the whole library during the month paid for. Films are for personal viewing only: no copying, recording or public showing.",
        ]),
        new("What you upload", [
            "You keep the rights to your films. By publishing one you allow us to show it on WatchingYou — in the galleries and on your profile — until you make it private or delete it.",
            "Upload only what you made or have the right to share, and say honestly whether it was made by hand or with AI. Every upload is reviewed; we may refuse or remove anything that breaks these rules or the law.",
        ]),
        new("Behaviour", [
            "Do not harass, threaten or deceive other members, send spam, or try to get into accounts or parts of the service that are not yours. Anyone can block and report; the Security desk reviews every report and may suspend accounts.",
        ]),
        new("Payments", [
            "Prices are shown before you pay. Card payments are processed by Stripe; we never see or store the full card number.",
        ]),
        new("Changes and liability", [
            "We may change the service and these terms; the current version is always on this page. We do our best to keep the service running, but cannot promise it will never be interrupted. Nothing in these terms limits rights you have under the law.",
        ]),
    ]);
}
