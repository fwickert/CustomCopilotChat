import {
    Body1,
    Button,
    Dialog,
    DialogBody,
    DialogContent,
    DialogSurface,
    DialogTitle,
    DialogTrigger,
    Label,
    Link,
    Subtitle1,
    Subtitle2,
    makeStyles,
    shorthands,
    tokens,
} from '@fluentui/react-components';
import { useState } from 'react';
import { useAppSelector } from '../../redux/app/hooks';
import { RootState } from '../../redux/app/store';
import { AppsAddIn24, Dismiss24 } from '../shared/BundledIcons';
import { AddPluginCard } from './cards/AddPluginCard';
import { PluginCard } from './cards/PluginCard';

const useClasses = makeStyles({
    root: {
        maxWidth: '1052px',
        height: '632px',
        width: 'fit-content',
        display: 'flex',
    },
    title: {
        ...shorthands.margin(0, 0, '12px'),
    },
    description: {
        ...shorthands.margin(0, 0, '12px'),
    },
    dialogContent: {
        ...shorthands.overflow('hidden'),
        display: 'flex',
        flexDirection: 'column',
    },
    content: {
        display: 'flex',
        flexDirection: 'row',
        flexWrap: 'wrap',
        overflowY: 'auto',
        rowGap: '24px',
        columnGap: '24px',
        ...shorthands.padding('12px', '2px', '12px'),
        '&::-webkit-scrollbar-thumb': {
            backgroundColor: tokens.colorScrollbarOverlay,
            visibility: 'visible',
        },
    },
});

export const PluginGallery: React.FC = () => {
    const classes = useClasses();

    const { plugins } = useAppSelector((state: RootState) => state.plugins);
    const [open, setOpen] = useState(false);

    return (
        <Dialog
            open={open}
            onOpenChange={(_event, data) => {
                setOpen(data.open);
            }}
        >
            <DialogTrigger>
                <Button
                    data-testid="pluginButton"
                    style={{ color: 'white' }}
                    appearance="transparent"
                    icon={<AppsAddIn24 color="white" />}
                    title="Plugins Gallery"
                    aria-label="Plugins Gallery"
                >
                    Plugins
                </Button>
            </DialogTrigger>
            <DialogSurface className={classes.root}>
                <DialogBody>
                    <DialogTitle
                        action={
                            <DialogTrigger action="close">
                                <Button
                                    data-testid="closeEnableCCPluginsPopUp"
                                    appearance="subtle"
                                    aria-label="close"
                                    icon={<Dismiss24 />}
                                />
                            </DialogTrigger>
                        }
                    >
                        <Subtitle1 block className={classes.title}>
                            Enable Chat Copilot Plugins
                        </Subtitle1>
                        <Body1 as="p" block className={classes.description}>
                            Authorize plugins and have more powerful bots!
                        </Body1>
                    </DialogTitle>
                    <DialogContent className={classes.dialogContent}>
                        <AddPluginCard />
                        <Subtitle2 block className={classes.title}>
                            Available Plugins
                        </Subtitle2>
                        <div className={classes.content}>
                            {Object.entries(plugins).map((entry) => {
                                const plugin = entry[1];
                                return <PluginCard key={plugin.name} plugin={plugin} />;
                            })}
                        </div>
                        <Label size="small" color="brand">
                            Want to learn more about plugins? Click{' '}
                            <Link href="https://aka.ms/sk-plugins-howto" target="_blank" rel="noreferrer">
                                here
                            </Link>
                            .
                        </Label>
                    </DialogContent>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
