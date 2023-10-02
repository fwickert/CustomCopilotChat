// Copyright (c) Microsoft. All rights reserved.

import { AuthenticatedTemplate, UnauthenticatedTemplate, useIsAuthenticated, useMsal } from '@azure/msal-react';
import { FluentProvider, Subtitle1, makeStyles, shorthands, tokens } from '@fluentui/react-components';

import * as React from 'react';
import { FC, useEffect } from 'react';
import { UserSettingsMenu } from './components/header/UserSettingsMenu';
import { BackendProbe, ChatView, Error, Loading, Login, UserConsent } from './components/views';
import { AuthHelper } from './libs/auth/AuthHelper';
import { useChat, useFile, useUserConsent } from './libs/hooks';
import { AlertType } from './libs/models/AlertType';
import { useAppDispatch, useAppSelector } from './redux/app/hooks';
import { RootState } from './redux/app/store';
import { FeatureKeys } from './redux/features/app/AppState';
import { addAlert, setActiveUserInfo, setServiceOptions } from './redux/features/app/appSlice';
import { semanticKernelDarkTheme, semanticKernelLightTheme } from './styles';


export const useClasses = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100vh',
        width: '100%',
        ...shorthands.overflow('hidden'),
    },
    header: {
        alignItems: 'center',
        backgroundColor: tokens.colorBrandForeground2,
        color: tokens.colorNeutralForegroundOnBrand,
        display: 'flex',
        '& h1': {
            paddingLeft: tokens.spacingHorizontalXL,
            display: 'flex',
        },
        height: '48px',
        justifyContent: 'space-between',
        width: '100%',
    },
    persona: {
        marginRight: tokens.spacingHorizontalXXL,
    },
    cornerItems: {
        display: 'flex',
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
});

enum AppState {
    ProbeForBackend,
    SettingUserInfo,
    ErrorLoadingUserInfo,
    LoadingChats,
    Chat,
    SigningOut,
    UserConsent
}

const App: FC = () => {
    const classes = useClasses();

    const [appState, setAppState] = React.useState(AppState.ProbeForBackend);
    const dispatch = useAppDispatch();

    const { instance, inProgress } = useMsal();
    const { activeUserInfo, features, isMaintenance } = useAppSelector((state: RootState) => state.app);
    const isAuthenticated = useIsAuthenticated();

    const chat = useChat();
    const file = useFile();

    //Add const for user consent useUserConsent
    const userconsent = useUserConsent();

    useEffect(() => {
        if (isMaintenance && appState !== AppState.ProbeForBackend) {
            setAppState(AppState.ProbeForBackend);
            return;
        }

        if (isAuthenticated) {
            if (appState === AppState.SettingUserInfo) {
                if (activeUserInfo === undefined) {
                    const account = instance.getActiveAccount();
                    if (!account) {
                        setAppState(AppState.ErrorLoadingUserInfo);
                    } else {
                        dispatch(
                            setActiveUserInfo({
                                id: `${account.localAccountId}.${account.tenantId}`,
                                email: account.username, // Username in an AccountInfo object is the email address
                                username: account.name ?? account.username,
                            }),
                        );

                        // Privacy disclaimer for internal Microsoft users
                        if (account.username.split('@')[1] === 'microsoft.com') {
                            dispatch(
                                addAlert({
                                    message:
                                        'By using Chat Copilot, you agree to protect sensitive data, not store it in chat, and allow chat history collection for service improvements. This tool is for internal use only.',
                                    type: AlertType.Info,
                                }),
                            );
                        }
                        setAppState(AppState.LoadingChats);
                        //setAppState(AppState.UserConsent);
                    }
                } else {
                    setAppState(AppState.LoadingChats);
                    //setAppState(AppState.UserConsent);
                }
            }
        }

        if ((isAuthenticated || !AuthHelper.IsAuthAAD) && appState === AppState.UserConsent)
        {
            void Promise.all([
                userconsent.getConsentAsync().then((succeeded) => {
                if (succeeded ) {
                    setAppState(AppState.LoadingChats);
                }
                }),
            ]);
        }

        if ((isAuthenticated || !AuthHelper.IsAuthAAD) && appState === AppState.LoadingChats) {
            
            void Promise.all([               

                // Load all chats from memory
                chat.loadChats().then((succeeded) => {
                    if (succeeded) {
                        setAppState(AppState.Chat);
                    }
                }),

                // Check if content safety is enabled
                file.getContentSafetyStatus(),

                // Load service options
                chat.getServiceOptions().then((serviceOptions) => {
                    if (serviceOptions) {
                        dispatch(setServiceOptions(serviceOptions));
                    }
                }),
            ]);
        
        }

        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [instance, inProgress, isAuthenticated, appState, isMaintenance]);

    // TODO: [Issue #41] handle error case of missing account information
    return (
        <FluentProvider
            className="app-container"
            theme={features[FeatureKeys.DarkMode].enabled ? semanticKernelDarkTheme : semanticKernelLightTheme}
        >
            {AuthHelper.IsAuthAAD ? (
                <>
                    <UnauthenticatedTemplate>
                        <div className={classes.container}>
                            <div className={classes.header}>
                                <Subtitle1 as="h1">Chat Copilot</Subtitle1>
                            </div>
                            {appState === AppState.SigningOut && <Loading text="Signing you out..." />}
                            {appState !== AppState.SigningOut && <Login />}
                        </div>
                    </UnauthenticatedTemplate>
                    <AuthenticatedTemplate>
                        <Chat classes={classes} appState={appState} setAppState={setAppState} />
                    </AuthenticatedTemplate>
                </>
            ) : (
                <Chat classes={classes} appState={appState} setAppState={setAppState} />
            )}
        </FluentProvider>
    );
};

const Chat = ({
    classes,
    appState,
    setAppState,
}: {
    classes: ReturnType<typeof useClasses>;
    appState: AppState;
    setAppState: (state: AppState) => void;
}) => {
    return (
        <div className={classes.container}>
            <div className={classes.header}>
                <Subtitle1 as="h1">Chat Copilot</Subtitle1>
                {appState > AppState.SettingUserInfo && (
                    <div className={classes.cornerItems}>
                        <div className={classes.cornerItems}>
                            {/*<PluginGallery />*/}
                            <UserSettingsMenu
                                setLoadingState={() => {
                                    setAppState(AppState.SigningOut);
                                }}
                            />
                        </div>
                    </div>
                )}
            </div>
            {appState === AppState.ProbeForBackend && (
                <BackendProbe
                    uri={process.env.REACT_APP_BACKEND_URI as string}
                    onBackendFound={() => {                        
                        if (AuthHelper.IsAuthAAD) {
                            setAppState(AppState.SettingUserInfo);
                        } else {
                            setAppState(AppState.LoadingChats);
                        }
                    }}
                />
            )}
            {appState === AppState.SettingUserInfo && (
                <Loading text={'Hang tight while we fetch your information...'} />
            )}
            {appState === AppState.ErrorLoadingUserInfo && (
                <Error text={'Oops, something went wrong. Please try signing out and signing back in.'} />
            )}
            {appState === AppState.LoadingChats && <Loading text="Loading Chats..." />}            
            {appState === AppState.Chat && <ChatView />}
            {appState === AppState.UserConsent && <UserConsent />}
           
        </div>
    );
};

export default App;
