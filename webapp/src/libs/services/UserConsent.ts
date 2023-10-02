import { IUserConsentDetails, UserConsent } from '../models/UserConsent';
import { BaseService } from './BaseService';

export class UserConsentService extends BaseService {
    public getConsentAsync = async (userId: string, accessToken: string): Promise<UserConsent> => {
        const result = await this.getResponseAsync<UserConsent>(
            {
                commandPath: `consent/getUserConsent/${userId}`,
                method: 'GET'
            },
            accessToken,
        );

        return result;
    };

    public updateConsentAsync = async (userConsent: IUserConsentDetails, accessToken: string): Promise<UserConsent> => {
        const result = await this.getResponseAsync<UserConsent>(
            {
                commandPath: `consent/updateUserConsent`,
                method: 'POST',
                body: userConsent
            },
            accessToken,
        );

        return result;
    }
}