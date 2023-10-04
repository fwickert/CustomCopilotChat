import { useMsal } from '@azure/msal-react';
import { AuthHelper } from '../auth/AuthHelper';
import { useAppDispatch, useAppSelector } from '../../redux/app/hooks';
import { RootState } from '../../redux/app/store';
import { addAlert } from '../../redux/features/app/appSlice';
import { AlertType } from '../models/AlertType';
import { UserConsentService } from '../services/UserConsent';
import { IUserConsentDetails } from '../models/UserConsent';


export const useUserConsent = () => {
    


    const dispatch = useAppDispatch();
    const { instance, inProgress } = useMsal();

    const userConsentService = new UserConsentService(process.env.REACT_APP_BACKEND_URI as string);
    const { activeUserInfo } = useAppSelector((state: RootState) => state.app);

    const userId = activeUserInfo?.id?? '';

    const consent: IUserConsentDetails = {
        id: userId,
        consent: true,
        consentDate: new Date().toUTCString(),
        consentVersion: '1.0.0.0'        
    }
    
    const getConsentAsync = async () => {
        try 
        {
            // Get SKaaS access token using AuthHelper
            const accessToken = await AuthHelper.getSKaaSAccessToken(instance, inProgress);
            
            // Get user consent using userConsentService
            return await userConsentService.getConsentAsync(userId, accessToken);          

        }
        catch (e: any)
        {
            // If there is an error, add an error alert to the UI
            const errorMessage = `Unable to load User consent. Details: ${getErrorDetails(e)}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));            
        }
        
        // If there is no user consent, return null
        return null;
    };

    const updateUserConsentAsync = async () => {
        try
        {
            const accessToken = await AuthHelper.getSKaaSAccessToken(instance, inProgress);          

            await userConsentService.updateConsentAsync(consent, accessToken);
        }
        catch (e: any)
        {
            const errorMessage = `Unable to save User consent. Details: ${getErrorDetails(e)}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));           
        }
        return null;
    };

    return { getConsentAsync, updateUserConsentAsync };
};

function getErrorDetails(e: any) {
    return e instanceof Error ? e.message : String(e);
}

