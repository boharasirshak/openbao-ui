import { useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Paper, Stack, TextField, Typography } from "@mui/material";
import { Link as RouterLink, Navigate, useParams } from "react-router-dom";
import { isAdmin, useSession } from "../App";
import {
  createMachineIdentity,
  createMember,
  createProject,
  deleteProject,
  disableMember,
  generateMachineSecretId,
  listAdminProjects,
  listAuditEvents,
  listMachineIdentities,
  listMembers,
  listRoles,
  revokeMachineSecretIds,
} from "../api/client";

const sections = ["projects", "members", "roles", "machine-identities", "audit"] as const;
type Section = (typeof sections)[number];

export function ProjectsPage() {
  const session = useSession();
  const projects = useQuery({
    queryKey: ["admin", "projects"],
    queryFn: listAdminProjects,
    enabled: isAdmin(session),
  });

  if (!isAdmin(session))
    return <Navigate replace to="/projects/thorneai/environments/development/secrets/backend" />;
  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h6">Projects</Typography>
      <Typography color="text.secondary" sx={{ mb: 2 }}>
        Open a project and navigate through its environment and collection URL.
      </Typography>
      <Stack spacing={1}>
        {(projects.data ?? []).map((project) => (
          <Button
            key={project.id}
            component={RouterLink}
            to={`/projects/${encodeURIComponent(project.id)}/environments/development/secrets/backend`}
            variant="outlined"
            sx={{ justifyContent: "flex-start" }}
          >
            {project.id} — {project.description || "no description"}
          </Button>
        ))}
      </Stack>
    </Paper>
  );
}

export function AdminPage({ section: forcedSection }: { section?: Section }) {
  const session = useSession();
  const { section: routeSection } = useParams();
  const section =
    forcedSection ??
    (sections.includes(routeSection as Section) ? (routeSection as Section) : "projects");

  if (!isAdmin(session)) return <Navigate replace to="/projects" />;
  return (
    <>
      <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: "wrap" }}>
        {sections.map((item) => (
          <Button
            key={item}
            component={RouterLink}
            to={item === "audit" ? "/audit" : `/admin/${item}`}
            variant={item === section ? "contained" : "text"}
          >
            {item.replaceAll("-", " ")}
          </Button>
        ))}
      </Stack>
      {section === "projects" && <ProjectAdmin />}
      {section === "members" && <MemberAdmin />}
      {section === "roles" && <RolesAdmin />}
      {section === "machine-identities" && <MachineAdmin />}
      {section === "audit" && <AuditPage />}
    </>
  );
}

function AdminPanel({
  title,
  children,
}: {
  title: string;
  children: (tools: {
    run: (action: () => Promise<void>, success: string) => Promise<void>;
  }) => ReactNode;
}) {
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const queryClient = useQueryClient();

  async function run(action: () => Promise<void>, success: string) {
    setError("");
    try {
      await action();
      setMessage(success);
      await queryClient.invalidateQueries({ queryKey: ["admin"] });
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : "Admin operation failed.");
    }
  }

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h6">{title}</Typography>
      {message && (
        <Alert severity="success" sx={{ mt: 2 }}>
          {message}
        </Alert>
      )}
      {error && (
        <Alert severity="error" sx={{ mt: 2 }}>
          {error}
        </Alert>
      )}
      {children({ run })}
    </Paper>
  );
}

function ProjectAdmin() {
  const [id, setId] = useState("");
  const [description, setDescription] = useState("");
  const projects = useQuery({ queryKey: ["admin", "projects"], queryFn: listAdminProjects });
  return (
    <AdminPanel title="Project administration">
      {({ run }) => (
        <>
          <Stack direction={{ xs: "column", md: "row" }} spacing={1} sx={{ mt: 2 }}>
            <TextField
              label="New project"
              value={id}
              onChange={(event) => setId(event.target.value)}
            />
            <TextField
              label="Description"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
            />
            <Button
              variant="contained"
              onClick={() =>
                run(async () => {
                  await createProject(id, description);
                  setId("");
                  setDescription("");
                }, "Project created.")
              }
            >
              Create project
            </Button>
          </Stack>
          <Stack spacing={1} sx={{ mt: 2 }}>
            {(projects.data ?? []).map((project) => (
              <Stack key={project.id} direction="row" spacing={1} alignItems="center">
                <Typography sx={{ flex: 1 }}>
                  {project.id} — {project.description || "no description"}
                </Typography>
                <Button
                  color="error"
                  onClick={() =>
                    window.confirm(`Delete ${project.id}?`) &&
                    run(() => deleteProject(project.id), "Project deleted.")
                  }
                >
                  Delete
                </Button>
              </Stack>
            ))}
          </Stack>
        </>
      )}
    </AdminPanel>
  );
}

function MemberAdmin() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const members = useQuery({ queryKey: ["admin", "members"], queryFn: listMembers });
  return (
    <AdminPanel title="Members">
      {({ run }) => (
        <>
          <Stack direction={{ xs: "column", md: "row" }} spacing={1} sx={{ mt: 2 }}>
            <TextField
              label="New member"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
            />
            <TextField
              label="Temporary password"
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <Button
              onClick={() =>
                run(async () => {
                  await createMember(username, password, ["default"]);
                  setUsername("");
                  setPassword("");
                }, "Member created.")
              }
            >
              Create member
            </Button>
          </Stack>
          <Stack spacing={1} sx={{ mt: 2 }}>
            {(members.data ?? []).map((member) => (
              <Stack key={member.username} direction="row" spacing={1} alignItems="center">
                <Typography sx={{ flex: 1 }}>
                  {member.username} — {member.disabled ? "disabled" : member.policies.join(", ")}
                </Typography>
                {!member.disabled && (
                  <Button
                    onClick={() => run(() => disableMember(member.username), "Member disabled.")}
                  >
                    Disable
                  </Button>
                )}
              </Stack>
            ))}
          </Stack>
        </>
      )}
    </AdminPanel>
  );
}

function RolesAdmin() {
  const roles = useQuery({ queryKey: ["admin", "roles"], queryFn: listRoles });
  return (
    <AdminPanel title="Built-in roles">
      {() => (
        <Stack spacing={1} sx={{ mt: 2 }}>
          {(roles.data ?? []).map((role) => (
            <Typography key={role.name}>
              {role.name} — {role.readOnly ? "read-only" : "editor"}
            </Typography>
          ))}
        </Stack>
      )}
    </AdminPanel>
  );
}

function MachineAdmin() {
  const [name, setName] = useState("");
  const [project, setProject] = useState("");
  const [environment, setEnvironment] = useState("production");
  const machines = useQuery({ queryKey: ["admin", "machines"], queryFn: listMachineIdentities });
  return (
    <AdminPanel title="Machine identities">
      {({ run }) => (
        <>
          <Stack direction={{ xs: "column", md: "row" }} spacing={1} sx={{ mt: 2 }}>
            <TextField
              label="Machine name"
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
            <TextField
              label="Project"
              value={project}
              onChange={(event) => setProject(event.target.value)}
            />
            <TextField
              label="Environment"
              value={environment}
              onChange={(event) => setEnvironment(event.target.value)}
            />
            <Button
              onClick={() =>
                run(async () => {
                  await createMachineIdentity(name, project, environment);
                  setName("");
                }, "Machine identity created.")
              }
            >
              Create machine
            </Button>
          </Stack>
          <Stack spacing={1} sx={{ mt: 2 }}>
            {(machines.data ?? []).map((machine) => (
              <Stack key={machine.name} direction="row" spacing={1} alignItems="center">
                <Typography sx={{ flex: 1 }}>
                  {machine.name} — {machine.project}/{machine.environment}
                </Typography>
                <Button
                  onClick={() =>
                    run(
                      async () =>
                        navigator.clipboard.writeText(await generateMachineSecretId(machine.name)),
                      "Secret ID generated and copied once.",
                    )
                  }
                >
                  Generate Secret ID
                </Button>
                <Button
                  onClick={() =>
                    run(() => revokeMachineSecretIds(machine.name), "Secret IDs revoked.")
                  }
                >
                  Revoke IDs
                </Button>
              </Stack>
            ))}
          </Stack>
        </>
      )}
    </AdminPanel>
  );
}

function AuditPage() {
  const audit = useQuery({ queryKey: ["admin", "audit"], queryFn: listAuditEvents });
  return (
    <AdminPanel title="Recent audit events">
      {() => (
        <Typography color="text.secondary" sx={{ mt: 2 }}>
          {audit.data?.length ?? 0} projected events loaded; secret values are never displayed.
        </Typography>
      )}
    </AdminPanel>
  );
}
