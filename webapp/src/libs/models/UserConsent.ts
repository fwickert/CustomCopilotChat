
export type UserConsent = Record<string, IUserConsentDetails>;

export interface IUserConsentDetails {
    id: string;
    consent: boolean;
    consentDate: string;
    consentVersion: string;
}

