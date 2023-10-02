import { FC } from 'react';
import { Body1, Body1Strong, Title3, Tooltip, Button, shorthands, tokens, makeStyles } from '@fluentui/react-components';

import { useSharedClasses } from '../../styles';
import { useUserConsent } from '../../libs/hooks/useUserConsent';
import { ChatView } from './ChatView';


const useClasses = makeStyles({
   
    uploadButton: {
        ...shorthands.margin('0', tokens.spacingHorizontalS, '0', '0'),
    }
   
});

export const UserConsent: FC = () => {
    const userConsent = useUserConsent();
    const classes = useClasses();
    const sharedClasses = useSharedClasses();

    return (
        <div className={sharedClasses.informativeView}>
            <Title3><strong>Consent </strong></Title3>
            <Body1>By using this internal secure chatgpt, you agree to the following terms and conditions:</Body1>
            <ul>
            <li>You are an authorized employee or contractor of the company that owns and operates this chatgpt.</li>
            <li>You will use this chatgpt only for work-related purposes and not for personal or illegal activities.</li>
            <li>You will respect the privacy and confidentiality of the information and data exchanged through this chatgpt and not disclose it to any unauthorized parties.</li>
            <li>You will comply with the company s policies and guidelines regarding the use of this chatgpt and report any violations or issues to the appropriate authorities.</li>
            <li>You understand that this chatgpt is powered by an artificial intelligence model that generates responses based on your input and context. The model is not perfect and may produce inaccurate, inappropriate, or offensive content. You will not hold the company or the model responsible for any harm or damage caused by such content.</li>
            <li>You understand that this chatgpt is not a substitute for human communication and does not provide any professional advice or guidance. You will use your own judgment and discretion when interacting with this chatgpt and not rely on it for any critical decisions or actions.</li>
            <li>You understand that this chatgpt is subject to change, improvement, or discontinuation at any time without prior notice. The company does not guarantee the availability, quality, or performance of this chatgpt and does not provide any warranty or liability for its use.</li>
            </ul>
            <Body1Strong>If you agree to these terms and conditions, please type click I agree to proceed. Thank you for using this internal secure chatgpt.</Body1Strong>

            <Tooltip content="Your are agree with this consent" relationship="label">
                    <Button
                        className={classes.uploadButton}                        
                        onClick={() => {
                            //void Promise.all([
                              // userConsent.updateUserConsentAsync()
                               //si l'update est ok
                               
                          //]);
                          // Swicth to Chatview view
                          //{<ChatView />}
                          userConsent.updateUserConsentAsync().then(() => {  
                            // Swicth to Chatview view
                            {<ChatView />}
                            }).catch((error) => {
                                console.log(error);
                            });
                       
                        }}
                    >
                        I agree
                    </Button>
            </Tooltip>
        </div>
    );
};

