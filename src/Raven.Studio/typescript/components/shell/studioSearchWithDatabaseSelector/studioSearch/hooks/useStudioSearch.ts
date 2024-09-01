import { useState, useRef } from "react";
import useBoolean from "components/hooks/useBoolean";
import { StudioSearchResultDatabaseGroup, StudioSearchResultItem } from "../studioSearchTypes";
import { useStudioSearchAsyncRegister } from "./useStudioSearchAsyncRegister";
import { useStudioSearchKeyboardEvents } from "./useStudioSearchKeyboardEvents";
import { useStudioSearchSyncRegister } from "./useStudioSearchSyncRegister";
import { useStudioSearchOmniSearch } from "./useStudioSearchOmniSearch";
import { useStudioSearchUtils } from "./useStudioSearchUtils";
import { useStudioSearchMouseEvents } from "./useStudioSearchMouseEvents";

export function useStudioSearch(menuItems: menuItem[]) {
    const { value: isSearchDropdownOpen, setValue: setIsDropdownOpen } = useBoolean(false);

    const dropdownRef = useRef<any>(null);
    const inputRef = useRef<HTMLInputElement>(null);

    const serverColumnRef = useRef<HTMLDivElement>(null);
    const databaseColumnRef = useRef<HTMLDivElement>(null);

    const [searchQuery, setSearchQuery] = useState("");
    const [activeItem, setActiveItem] = useState<StudioSearchResultItem>(null);

    const { register, results } = useStudioSearchOmniSearch(searchQuery);

    const refs = {
        inputRef,
        dropdownRef,
        serverColumnRef,
        databaseColumnRef,
    };

    const { goToUrl, resetDropdown } = useStudioSearchUtils({
        inputRef,
        setIsDropdownOpen,
        setSearchQuery,
        setActiveItem,
    });

    useStudioSearchSyncRegister({
        register,
        menuItems,
        goToUrl,
        resetDropdown,
    });

    useStudioSearchAsyncRegister({
        register,
        searchQuery,
        goToUrl,
    });

    useStudioSearchKeyboardEvents({
        refs,
        studioSearchInputId,
        results,
        activeItem,
        setIsDropdownOpen,
        setActiveItem,
        setSearchQuery,
    });

    useStudioSearchMouseEvents({
        inputRef,
        studioSearchBackdropId,
        setIsDropdownOpen,
    });

    const matchStatus = {
        hasServerMatch: results.server.length > 0,
        hasSwitchToDatabaseMatch: results.switchToDatabase.length > 0,
        hasDatabaseMatch: Object.keys(results.database).some(
            (groupType: StudioSearchResultDatabaseGroup) => results.database[groupType].length > 0
        ),
    };

    return {
        refs,
        isSearchDropdownOpen,
        searchQuery,
        setSearchQuery,
        matchStatus,
        results,
        activeItem,
    };
}

export const studioSearchInputId = "studio-search-input";
export const studioSearchBackdropId = "studio-search-backdrop";
