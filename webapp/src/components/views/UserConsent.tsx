import { makeStyles, shorthands } from '@fluentui/react-components';
import { FC } from 'react';

const useClasses = makeStyles({
    container: {
        ...shorthands.overflow('hidden'),
        display: 'flex',
        flexDirection: 'row',
        alignContent: 'start',
        height: '100%',
    },
});

export const UserConsent: FC = () => {
    
    const classes = useClasses();

    return (
        <div className={classes.container}>
            COUCOU
            
        </div>
    );
};
