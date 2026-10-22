export type ShortFilmOrigin = "AiGenerated" | "HandCrafted";
export type ShortFilmVisibility = "Private" | "Public";
export type SubmissionStatus =
  | "Pending" | "UnderSecurityReview" | "SecurityCleared" | "SecurityFlagged"
  | "Approved" | "Rejected" | "Expired";

export type SecurityCheckName =
  | "Copyright" | "SexualContent" | "GraphicViolence" | "MinorsWithoutConsent"
  | "HateOrExtremism" | "PersonalData" | "FileIntegrity" | "OriginDeclaration";

export type CheckOutcome = "Pass" | "Fail" | "NotApplicable";

export interface ShortFilmSummary {
  id: string;
  title: string;
  synopsis: string;
  authorName: string;
  origin: ShortFilmOrigin;
  visibility: ShortFilmVisibility;
  status: SubmissionStatus;
  submittedAtUtc: string;
  reviewDeadlineUtc: string;
  approvedAtUtc?: string | null;
  viewCount: number;
  sizeBytes: number;
  originalFileName: string;
  contentType: string;
  hoursLeft: number;
  reviewerNote?: string | null;
  streamUrl: string;
}

export interface SecurityCheckDto {
  check: SecurityCheckName;
  outcome: CheckOutcome;
  note?: string | null;
}

export interface SecurityReportDto {
  reviewerName: string;
  verdict: "Cleared" | "Flagged";
  summary?: string | null;
  watchedInFull: boolean;
  completedAtUtc: string;
  checks: SecurityCheckDto[];
}

export interface SubmissionCommentDto {
  id: string;
  authorName: string;
  authorRole: string;
  body: string;
  createdAtUtc: string;
}

export interface ShortFilmDetail {
  film: ShortFilmSummary;
  report?: SecurityReportDto | null;
  comments: SubmissionCommentDto[];
}

/** The checklist, in the order Security works through it. */
export const SECURITY_CHECKS: SecurityCheckName[] = [
  "Copyright",
  "SexualContent",
  "GraphicViolence",
  "MinorsWithoutConsent",
  "HateOrExtremism",
  "PersonalData",
  "FileIntegrity",
  "OriginDeclaration",
];

export const statusTone = (status: SubmissionStatus): "good" | "warn" | "bad" =>
  status === "Approved" ? "good"
  : status === "Rejected" || status === "SecurityFlagged" ? "bad"
  : "warn";

export const megabytes = (bytes: number) => `${(bytes / 1_048_576).toFixed(1)} MB`;
