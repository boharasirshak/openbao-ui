"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Avatar,
  Box,
  CircularProgress,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Stack,
  Toolbar,
  Tooltip,
  Typography,
} from "@mui/material";
import FolderIcon from "@mui/icons-material/FolderOutlined";
import PeopleIcon from "@mui/icons-material/PeopleAltOutlined";
import GroupsIcon from "@mui/icons-material/GroupsOutlined";
import SmartToyIcon from "@mui/icons-material/SmartToyOutlined";
import StorageIcon from "@mui/icons-material/StorageOutlined";
import LogoutIcon from "@mui/icons-material/LogoutOutlined";
import MenuIcon from "@mui/icons-material/Menu";
import LockIcon from "@mui/icons-material/LockOutlined";
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { getSession, logout, type SessionResponse } from "@/lib/client";
import ThemeModeToggle from "@/components/ui/ThemeModeToggle";
import { keys } from "@/lib/queryKeys";

const DRAWER_WIDTH = 248;

const SessionContext = createContext<SessionResponse | null>(null);

export function useSession() {
  return useContext(SessionContext);
}

export function isAdmin(session: SessionResponse | null) {
  return Boolean(
    session?.policies?.some((policy) => policy === "wrapper-admin" || policy === "root"),
  );
}

type NavItem = { href: string; label: string; icon: ReactNode; adminOnly?: boolean };

const NAV: { heading: string; items: NavItem[] }[] = [
  {
    heading: "Workspace",
    items: [
      { href: "/projects", label: "Projects", icon: <FolderIcon fontSize="small" /> },
      { href: "/database", label: "Database creds", icon: <StorageIcon fontSize="small" /> },
    ],
  },
  {
    heading: "Access control",
    items: [
      {
        href: "/admin/members",
        label: "Members",
        icon: <PeopleIcon fontSize="small" />,
        adminOnly: true,
      },
      {
        href: "/admin/teams",
        label: "Teams",
        icon: <GroupsIcon fontSize="small" />,
        adminOnly: true,
      },
      {
        href: "/admin/machine-identities",
        label: "Machine identities",
        icon: <SmartToyIcon fontSize="small" />,
        adminOnly: true,
      },
    ],
  },
];

function expiresIn(session: SessionResponse | null): string {
  if (!session?.expiresAt) return "";
  const minutes = Math.round((new Date(session.expiresAt).getTime() - Date.now()) / 60_000);
  if (minutes <= 0) return "expiring now";
  if (minutes < 60) return `expires in ${minutes}m`;
  return `expires in ${Math.round(minutes / 60)}h`;
}

function SidebarContent({
  session,
  pathname,
  onNavigate,
  onSignOut,
}: {
  session: SessionResponse;
  pathname: string;
  onNavigate: () => void;
  onSignOut: () => void;
}) {
  const admin = isAdmin(session);

  return (
    <Stack sx={{ height: "100%" }}>
      <Toolbar sx={{ gap: 1.5, px: 2.5 }}>
        <LockIcon sx={{ color: "primary.main" }} />
        <Box>
          <Typography sx={{ fontWeight: 600, lineHeight: 1.2 }}>OpenBao</Typography>
          <Typography variant="caption" color="text.secondary">
            Developer secrets
          </Typography>
        </Box>
      </Toolbar>
      <Divider />

      <Box sx={{ flex: 1, overflowY: "auto", py: 1 }}>
        {NAV.map((group) => {
          const items = group.items.filter((item) => admin || !item.adminOnly);
          if (items.length === 0) return null;
          return (
            <Box key={group.heading} sx={{ mb: 1 }}>
              <Typography
                variant="caption"
                sx={{
                  px: 2.5,
                  color: "text.secondary",
                  letterSpacing: 0.6,
                  textTransform: "uppercase",
                  fontSize: 11,
                }}
              >
                {group.heading}
              </Typography>
              <List dense disablePadding sx={{ mt: 0.5 }}>
                {items.map((item) => (
                  <ListItemButton
                    key={item.href}
                    component={Link}
                    href={item.href}
                    onClick={onNavigate}
                    selected={pathname.startsWith(item.href)}
                    sx={{
                      mx: 1.5,
                      borderRadius: 1,
                      "&.Mui-selected": {
                        bgcolor: "action.selected",
                        "& .MuiListItemIcon-root": { color: "primary.main" },
                      },
                    }}
                  >
                    <ListItemIcon sx={{ minWidth: 34, color: "text.secondary" }}>
                      {item.icon}
                    </ListItemIcon>
                    <ListItemText primary={item.label} />
                  </ListItemButton>
                ))}
              </List>
            </Box>
          );
        })}
      </Box>

      <Divider />
      <Stack direction="row" spacing={1.5} alignItems="center" sx={{ p: 2 }}>
        <Avatar
          sx={{ width: 32, height: 32, bgcolor: "primary.main", color: "primary.contrastText" }}
        >
          {admin ? "A" : "U"}
        </Avatar>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" noWrap>
            {admin ? "Administrator" : "Signed in"}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap>
            Session {expiresIn(session)}
          </Typography>
        </Box>
        <ThemeModeToggle />
        <Tooltip title="Sign out">
          <IconButton size="small" onClick={onSignOut}>
            <LogoutIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Stack>
    </Stack>
  );
}

export default function AppShell({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const queryClient = useQueryClient();
  const [mobileOpen, setMobileOpen] = useState(false);
  const session = useQuery({ queryKey: keys.session, queryFn: getSession, staleTime: 30_000 });

  const isPublic = pathname === "/login" || pathname.startsWith("/share/");
  const signedOut = !isPublic && !session.isLoading && !session.data;
  useEffect(() => {
    // Redirecting during render warns and can loop, so do it after paint.
    if (signedOut) router.replace("/login");
  }, [signedOut, router]);

  async function signOut() {
    try {
      await logout();
    } finally {
      queryClient.clear();
      router.replace("/login");
    }
  }

  // Pages that must render without a session. A share link is opened by whoever
  // received it, who has no account here.
  if (pathname === "/login" || pathname.startsWith("/share/")) return <>{children}</>;

  if (session.isLoading) {
    return (
      <Stack alignItems="center" justifyContent="center" sx={{ height: "100vh" }} spacing={2}>
        <CircularProgress size={28} />
        <Typography color="text.secondary">Checking your session…</Typography>
      </Stack>
    );
  }

  if (!session.data) {
    return (
      <Stack alignItems="center" justifyContent="center" sx={{ height: "100vh" }}>
        <Typography color="text.secondary">Taking you to sign in…</Typography>
      </Stack>
    );
  }

  const sidebar = (
    <SidebarContent
      session={session.data}
      pathname={pathname}
      onNavigate={() => setMobileOpen(false)}
      onSignOut={signOut}
    />
  );

  return (
    <SessionContext.Provider value={session.data}>
      <Box sx={{ display: "flex", minHeight: "100vh" }}>
        <Drawer
          variant="permanent"
          sx={{
            width: DRAWER_WIDTH,
            flexShrink: 0,
            display: { xs: "none", md: "block" },
            "& .MuiDrawer-paper": { width: DRAWER_WIDTH, boxSizing: "border-box" },
          }}
        >
          {sidebar}
        </Drawer>
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={{
            display: { xs: "block", md: "none" },
            "& .MuiDrawer-paper": { width: DRAWER_WIDTH, boxSizing: "border-box" },
          }}
        >
          {sidebar}
        </Drawer>

        <Box component="main" sx={{ flex: 1, minWidth: 0, px: { xs: 2, md: 4 }, py: 3 }}>
          <IconButton
            onClick={() => setMobileOpen(true)}
            sx={{ display: { md: "none" }, mb: 1 }}
            aria-label="Open navigation"
          >
            <MenuIcon />
          </IconButton>
          {children}
        </Box>
      </Box>
    </SessionContext.Provider>
  );
}

export function AccessDenied({ what = "this page" }: { what?: string }) {
  return (
    <Alert severity="warning" sx={{ mt: 2 }}>
      You need administrator access to view {what}. Ask an administrator to grant you the
      <code> wrapper-admin </code> policy.
    </Alert>
  );
}

export function PageHeader({
  title,
  description,
  actions,
}: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <Stack
      direction={{ xs: "column", sm: "row" }}
      spacing={2}
      alignItems={{ sm: "flex-start" }}
      justifyContent="space-between"
      sx={{ mb: 3 }}
    >
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="h5">{title}</Typography>
        {description && (
          <Typography color="text.secondary" variant="body2" sx={{ mt: 0.5 }}>
            {description}
          </Typography>
        )}
      </Box>
      {actions && (
        <Stack direction="row" spacing={1} sx={{ flexShrink: 0 }}>
          {actions}
        </Stack>
      )}
    </Stack>
  );
}

export function LoadingRow({ label = "Loading…" }: { label?: string }) {
  return (
    <Stack direction="row" spacing={1.5} alignItems="center" sx={{ py: 4, px: 2 }}>
      <CircularProgress size={18} />
      <Typography color="text.secondary" variant="body2">
        {label}
      </Typography>
    </Stack>
  );
}

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <Stack alignItems="center" spacing={0.5} sx={{ py: 6 }}>
      <Typography color="text.secondary">{title}</Typography>
      {hint && (
        <Typography variant="body2" sx={{ color: "text.disabled" }}>
          {hint}
        </Typography>
      )}
    </Stack>
  );
}
