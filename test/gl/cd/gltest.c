/*
 * OpenGL smoke test for the emulated Juniper: which renderer an ordinary
 * GLUT window gets, and whether a clear and a triangle land.
 * The first frame is read back; then the triangle spins for 20 seconds.
 *   gcc -o gltest gltest.c -framework GLUT -framework OpenGL && ./gltest
 */
#include <GLUT/glut.h>
#include <OpenGL/OpenGL.h>
#include <stdio.h>
#include <stdlib.h>

#include <sys/time.h>

static int frames;
static double t0;

static double now(void)
{
	struct timeval tv;

	gettimeofday(&tv, NULL);
	return tv.tv_sec + tv.tv_usec / 1e6;
}

static void show(void)
{
	unsigned char px[4][4];
	double t = now();

	if (!t0)
		t0 = t;
	glClearColor(1, 0, 0, 1);
	glClear(GL_COLOR_BUFFER_BIT);
	glLoadIdentity();
	if (frames)
		glRotatef((float)((t - t0) * 90.0), 0, 0, 1);
	glBegin(GL_TRIANGLES);
	glColor3f(0, 1, 0); glVertex2f(-0.8f, -0.8f);
	glColor3f(0, 0, 1); glVertex2f(0.8f, -0.8f);
	glColor3f(1, 1, 1); glVertex2f(0.0f, 0.8f);
	glEnd();
	glFinish();
	glReadPixels(128, 100, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, px[0]);
	glReadPixels(5, 250, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, px[1]);
	if (frames++ == 0) {
		printf("centre %d %d %d, corner %d %d %d (want a mix of green/blue/white, then red)\n",
		       px[0][0], px[0][1], px[0][2], px[1][0], px[1][1], px[1][2]);
		fflush(stdout);
	}
	glutSwapBuffers();
	if (t - t0 > 20) {
		printf("%d frames in %.1f s: %.1f fps\n", frames, t - t0, frames / (t - t0));
		exit(0);
	}
	glutPostRedisplay();
}

int main(int argc, char **argv)
{
	GLint id = 0;

	glutInit(&argc, argv);
	glutInitDisplayMode(GLUT_RGBA | GLUT_DOUBLE);
	glutInitWindowSize(256, 256);
	glutInitWindowPosition(700, 100);
	glutCreateWindow("gltest");
	CGLGetParameter(CGLGetCurrentContext(), kCGLCPCurrentRendererID, &id);
	printf("vendor   %s\nrenderer %s\nversion  %s\nrenderer id 0x%x\n",
	       glGetString(GL_VENDOR), glGetString(GL_RENDERER), glGetString(GL_VERSION), id);
	fflush(stdout);
	glutDisplayFunc(show);
	glutMainLoop();
	return 0;
}
