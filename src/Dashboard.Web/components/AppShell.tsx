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
import BackIcon from "@mui/icons-material/ArrowBackOutlined";
import SearchIcon from "@mui/icons-material/SearchOutlined";
import GridIcon from "@mui/icons-material/GridViewOutlined";
import ApprovalIcon from "@mui/icons-material/RuleOutlined";
import HistoryIcon from "@mui/icons-material/HistoryOutlined";
import SettingsIcon from "@mui/icons-material/SettingsOutlined";
import ShieldIcon from "@mui/icons-material/VerifiedUserOutlined";
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { getSession, logout, type SessionResponse } from "@/lib/client";
import ThemeModeToggle from "@/components/ui/ThemeModeToggle";
import { keys } from "@/lib/queryKeys";
import { useProjectEnvironments } from "@/lib/useProjectEnvironments";

const DRAWER_WIDTH = 248;

const SessionContext = createContext<SessionResponse | null>(null);

/**
 * The project the URL is inside, or null on the projects list and the admin pages.
 * Decides whether the sidebar shows the workspace or one project's own sections.
 */
function projectInPath(pathname: string): string | null {
  const match = /^\/projects\/([^/]+)/.exec(pathname);
  return match ? decodeURIComponent(match[1]) : null;
}

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

/** The project's own sections, in the order someone actually reaches for them. */
const PROJECT_NAV: { slug: string; label: string; icon: ReactNode }[] = [
  { slug: "search", label: "Search", icon: <SearchIcon fontSize="small" /> },
  { slug: "changes", label: "Approvals", icon: <ApprovalIcon fontSize="small" /> },
  { slug: "members", label: "Members", icon: <PeopleIcon fontSize="small" /> },
  { slug: "activity", label: "Activity", icon: <HistoryIcon fontSize="small" /> },
  { slug: "roles", label: "Roles", icon: <ShieldIcon fontSize="small" /> },
  { slug: "settings", label: "Settings", icon: <SettingsIcon fontSize="small" /> },
];

/** Path segments under a project that are pages of their own, not overview folders. */
const PROJECT_SECTIONS = ["environments", "compare", ...PROJECT_NAV.map((item) => item.slug)];

function SidebarHeading({ children }: { children: ReactNode }) {
  return (
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
      {children}
    </Typography>
  );
}

function SidebarItem({
  href,
  label,
  icon,
  selected,
  onNavigate,
  trailing,
}: {
  href: string;
  label: ReactNode;
  icon?: ReactNode;
  selected: boolean;
  onNavigate: () => void;
  trailing?: ReactNode;
}) {
  return (
    <ListItemButton
      component={Link}
      href={href}
      onClick={onNavigate}
      selected={selected}
      sx={{
        mx: 1.5,
        borderRadius: 1,
        "&.Mui-selected": {
          bgcolor: "action.selected",
          "& .MuiListItemIcon-root": { color: "primary.main" },
        },
      }}
    >
      {icon && <ListItemIcon sx={{ minWidth: 34, color: "text.secondary" }}>{icon}</ListItemIcon>}
      <ListItemText primary={label} />
      {trailing}
    </ListItemButton>
  );
}

/**
 * Everything about one project, shown while you are inside it. The project's sections
 * used to be six identical text links in the page header, which meant they were only
 * reachable from the project page — going from a secret to Activity took a trip back up.
 */
function ProjectNav({
  project,
  pathname,
  onNavigate,
}: {
  project: string;
  pathname: string;
  onNavigate: () => void;
}) {
  const { environments } = useProjectEnvironments(project);
  const root = `/projects/${encodeURIComponent(project)}`;
  // The environment currently open, so the sidebar shows where you are.
  const current = /\/environments\/([^/]+)/.exec(pathname)?.[1];
  // Overview owns every path that is not one of the named sections.
  const onOverview =
    pathname === root ||
    (pathname.startsWith(`${root}/`) &&
      !PROJECT_SECTIONS.some((section) => pathname.startsWith(`${root}/${section}`)));

  return (
    <>
      <Box sx={{ mb: 1 }}>
        <List dense disablePadding>
          <SidebarItem
            href="/projects"
            label="All projects"
            icon={<BackIcon fontSize="small" />}
            selected={false}
            onNavigate={onNavigate}
          />
        </List>
        <Typography sx={{ px: 2.5, pt: 1.5, fontWeight: 600 }} noWrap title={project}>
          {project}
        </Typography>
        <List dense disablePadding sx={{ mt: 0.5 }}>
          <SidebarItem
            href={root}
            label="Overview"
            icon={<GridIcon fontSize="small" />}
            selected={onOverview}
            onNavigate={onNavigate}
          />
        </List>
      </Box>

      <Box sx={{ mb: 1 }}>
        <SidebarHeading>Environments</SidebarHeading>
        <List dense disablePadding sx={{ mt: 0.5 }}>
          {environments.map((environment) => (
            <SidebarItem
              key={environment.id}
              href={`${root}/environments/${encodeURIComponent(environment.id)}/secrets`}
              label={environment.displayName}
              icon={<FolderIcon fontSize="small" />}
              selected={current === environment.id}
              onNavigate={onNavigate}
              trailing={
                environment.protected ? (
                  <Tooltip title="Changes here need approval">
                    <ShieldIcon sx={{ fontSize: 15, color: "warning.main" }} />
                  </Tooltip>
                ) : undefined
              }
            />
          ))}
        </List>
      </Box>

      <Box sx={{ mb: 1 }}>
        <SidebarHeading>Project</SidebarHeading>
        <List dense disablePadding sx={{ mt: 0.5 }}>
          {PROJECT_NAV.map((item) => (
            <SidebarItem
              key={item.slug}
              href={`${root}/${item.slug}`}
              label={item.label}
              icon={item.icon}
              selected={pathname.startsWith(`${root}/${item.slug}`)}
              onNavigate={onNavigate}
            />
          ))}
        </List>
      </Box>
    </>
  );
}

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
  project,
  onNavigate,
  onSignOut,
}: {
  session: SessionResponse;
  pathname: string;
  project: string | null;
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
        {project ? (
          <ProjectNav project={project} pathname={pathname} onNavigate={onNavigate} />
        ) : (
          NAV.map((group) => {
            const items = group.items.filter((item) => admin || !item.adminOnly);
            if (items.length === 0) return null;
            return (
              <Box key={group.heading} sx={{ mb: 1 }}>
                <SidebarHeading>{group.heading}</SidebarHeading>
                <List dense disablePadding sx={{ mt: 0.5 }}>
                  {items.map((item) => (
                    <SidebarItem
                      key={item.href}
                      href={item.href}
                      label={item.label}
                      icon={item.icon}
                      selected={pathname.startsWith(item.href)}
                      onNavigate={onNavigate}
                    />
                  ))}
                </List>
              </Box>
            );
          })
        )}
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
      project={projectInPath(pathname)}
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
