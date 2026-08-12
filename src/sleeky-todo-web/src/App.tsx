import { useState } from 'react'

import './App.css'
import {
  ApiError,
  createTodo,
  deleteTodo,
  getTodo,
  restoreTodo,
  updateTodo,
} from './api/todos.ts'
import { TodoCard } from './components/TodoCard.tsx'
import { TodoForm } from './components/TodoForm.tsx'
import type { ProblemDetails, Todo, TodoDraft } from './types/todo.ts'

type UiError = {
  affectedTodoId?: string
  problem: ProblemDetails
}

function localDeletion(todo: Todo): Todo {
  const deletedAt = new Date()
  const purgeAt = new Date(deletedAt)
  purgeAt.setUTCDate(purgeAt.getUTCDate() + 90)

  return {
    ...todo,
    version: todo.version + 1,
    updatedAt: deletedAt.toISOString(),
    deletedAt: deletedAt.toISOString(),
    purgeAt: purgeAt.toISOString(),
  }
}

function App() {
  const [todos, setTodos] = useState<Todo[]>([])
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<UiError | null>(null)

  const activeTodos = todos.filter((todo) => todo.deletedAt === null)
  const deletedTodos = todos.filter((todo) => todo.deletedAt !== null)

  function replaceTodo(updatedTodo: Todo) {
    setTodos((current) => current.map((todo) => (
      todo.id === updatedTodo.id ? updatedTodo : todo
    )))
  }

  function captureError(caught: unknown, affectedTodoId?: string) {
    const problem = caught instanceof ApiError
      ? caught.problem
      : { title: 'Unable to reach the API.', detail: 'Check that the API and MongoDB are running.' }
    setError({ affectedTodoId, problem })
  }

  async function handleCreate(draft: TodoDraft): Promise<boolean> {
    setBusyId('create')
    setError(null)

    try {
      const created = await createTodo(draft)
      setTodos((current) => [created, ...current])
      return true
    } catch (caught) {
      captureError(caught)
      return false
    } finally {
      setBusyId(null)
    }
  }

  async function handleUpdate(todo: Todo, draft: TodoDraft): Promise<boolean> {
    setBusyId(todo.id)
    setError(null)

    try {
      replaceTodo(await updateTodo(todo, draft))
      return true
    } catch (caught) {
      captureError(caught, todo.id)
      return false
    } finally {
      setBusyId(null)
    }
  }

  async function handleDelete(todo: Todo) {
    setBusyId(todo.id)
    setError(null)

    try {
      await deleteTodo(todo)
      replaceTodo(localDeletion(todo))
    } catch (caught) {
      captureError(caught, todo.id)
    } finally {
      setBusyId(null)
    }
  }

  async function handleRestore(todo: Todo) {
    setBusyId(todo.id)
    setError(null)

    try {
      replaceTodo(await restoreTodo(todo))
    } catch (caught) {
      captureError(caught, todo.id)
    } finally {
      setBusyId(null)
    }
  }

  async function refreshAffectedTodo() {
    if (!error?.affectedTodoId) {
      return
    }

    setBusyId(error.affectedTodoId)
    try {
      replaceTodo(await getTodo(error.affectedTodoId))
      setError(null)
    } catch (caught) {
      captureError(caught, error.affectedTodoId)
    } finally {
      setBusyId(null)
    }
  }

  return (
    <main className="app-shell">
      <header className="page-header">
        <div>
          <p className="eyebrow">Sleeky To-Do</p>
          <h1>Keep today clear.</h1>
          <p>Create a focused list, make changes safely, and recover deleted work.</p>
        </div>
        <div className="session-note">
          <strong>First shell</strong>
          <span>Shows TODOs created in this browser session.</span>
        </div>
      </header>

      {error && (
        <section className="error-banner" role="alert">
          <div>
            <strong>{error.problem.title ?? 'Something went wrong.'}</strong>
            <p>{error.problem.detail}</p>
            {error.problem.traceId && <small>Trace: {error.problem.traceId}</small>}
          </div>
          {error.problem.status === 409 && error.affectedTodoId && (
            <button
              className="button secondary"
              type="button"
              onClick={() => void refreshAffectedTodo()}
            >
              Load latest version
            </button>
          )}
        </section>
      )}

      <section className="create-panel">
        <TodoForm
          busy={busyId === 'create'}
          errors={!error?.affectedTodoId ? error?.problem.errors : undefined}
          legend="Create a TODO"
          submitLabel="Add TODO"
          onSubmit={handleCreate}
        />
      </section>

      <section className="todo-section" aria-labelledby="active-heading">
        <div className="section-heading">
          <h2 id="active-heading">Active</h2>
          <span>{activeTodos.length}</span>
        </div>
        {activeTodos.length === 0 ? (
          <p className="empty-state">No active TODOs yet. Add the first one above.</p>
        ) : (
          <div className="todo-grid">
            {activeTodos.map((todo) => (
              <TodoCard
                key={`${todo.id}:${todo.version}`}
                busy={busyId === todo.id}
                errors={error?.affectedTodoId === todo.id ? error.problem.errors : undefined}
                todo={todo}
                onDelete={handleDelete}
                onRestore={handleRestore}
                onUpdate={handleUpdate}
              />
            ))}
          </div>
        )}
      </section>

      {deletedTodos.length > 0 && (
        <section className="todo-section" aria-labelledby="deleted-heading">
          <div className="section-heading">
            <h2 id="deleted-heading">Recently deleted</h2>
            <span>{deletedTodos.length}</span>
          </div>
          <div className="todo-grid">
            {deletedTodos.map((todo) => (
              <TodoCard
                key={`${todo.id}:${todo.version}`}
                busy={busyId === todo.id}
                todo={todo}
                onDelete={handleDelete}
                onRestore={handleRestore}
                onUpdate={handleUpdate}
              />
            ))}
          </div>
        </section>
      )}
    </main>
  )
}

export default App
